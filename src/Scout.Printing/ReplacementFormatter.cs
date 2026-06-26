using System.Text;

namespace Scout;

internal static class ReplacementFormatter
{
    private const int CaptureAtomLiteral = 0;
    private const int CaptureAtomClass = 1;
    private const int CaptureAtomDot = 2;
    private const int CaptureAtomDigit = 3;
    private const int CaptureAtomNotDigit = 4;
    private const int CaptureAtomWord = 5;
    private const int CaptureAtomNotWord = 6;
    private const int CaptureAtomWhitespace = 7;
    private const int CaptureAtomNotWhitespace = 8;
    private const int CaptureClassAlnum = 9;
    private const int CaptureClassAlpha = 10;
    private const int CaptureClassAscii = 11;
    private const int CaptureClassBlank = 12;
    private const int CaptureClassControl = 13;
    private const int CaptureClassGraph = 14;
    private const int CaptureClassLower = 15;
    private const int CaptureClassPrint = 16;
    private const int CaptureClassPunct = 17;
    private const int CaptureClassUpper = 18;
    private const int CaptureClassHexDigit = 19;

    public static byte[] ReplaceLine(
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        List<long> replacementColumns,
        List<int>? replacementLengths = null,
        ReplacementCapturePlan? capturePlan = null,
        ReplacementTemplate? template = null,
        int[]? captureStartsBuffer = null,
        int[]? captureLengthsBuffer = null,
        Dictionary<string, int>? captureNamesBuffer = null)
    {
        template ??= ReplacementTemplate.Create(replacement, patterns);
        replacementColumns.Clear();
        replacementLengths?.Clear();
        var bytes = new List<byte>(line.Length);
        int previous = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            int start = starts[index];
            Add(bytes, line[previous..start]);
            replacementColumns.Add(bytes.Count + 1L);
            int replacementStart = bytes.Count;
            AddExpanded(
                bytes,
                replacement,
                line.Slice(start, lengths[index]),
                patterns,
                asciiCaseInsensitive,
                capturePlan,
                template,
                captureStartsBuffer,
                captureLengthsBuffer,
                captureNamesBuffer);
            replacementLengths?.Add(bytes.Count - replacementStart);
            previous = start + lengths[index];
        }

        Add(bytes, line[previous..]);
        return bytes.ToArray();
    }

    public static byte[] Expand(
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> matched,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan = null,
        ReplacementTemplate? template = null,
        int[]? captureStartsBuffer = null,
        int[]? captureLengthsBuffer = null,
        Dictionary<string, int>? captureNamesBuffer = null)
    {
        template ??= ReplacementTemplate.Create(replacement, patterns);
        var bytes = new List<byte>(template.LiteralLength + matched.Length);
        AddExpanded(
            bytes,
            replacement,
            matched,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureStartsBuffer,
            captureLengthsBuffer,
            captureNamesBuffer);
        return bytes.ToArray();
    }

    public static void WriteReplacedLine(
        RawByteWriter output,
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan = null,
        ReplacementTemplate? template = null,
        int[]? captureStartsBuffer = null,
        int[]? captureLengthsBuffer = null,
        Dictionary<string, int>? captureNamesBuffer = null)
    {
        template ??= ReplacementTemplate.Create(replacement, patterns);
        int previous = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            int start = starts[index];
            output.Write(line[previous..start]);
            WriteExpanded(
                output,
                replacement,
                line.Slice(start, lengths[index]),
                patterns,
                asciiCaseInsensitive,
                capturePlan,
                template,
                captureStartsBuffer,
                captureLengthsBuffer,
                captureNamesBuffer);
            previous = start + lengths[index];
        }

        output.Write(line[previous..]);
    }

    private static void AddExpanded(
        List<byte> bytes,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> matched,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan,
        ReplacementTemplate? template,
        int[]? captureStartsBuffer,
        int[]? captureLengthsBuffer,
        Dictionary<string, int>? captureNamesBuffer)
    {
        template ??= ReplacementTemplate.Create(replacement, patterns);
        if (capturePlan?.TryAddExpandedNumericReplacement(bytes, matched, template) == true)
        {
            return;
        }

        int[] captureStarts = captureStartsBuffer ?? CreateCaptureArray(template.HighestCapture);
        int[] captureLengths = captureLengthsBuffer ?? CreateCaptureArray(template.HighestCapture);
        Dictionary<string, int>? captureNames = template.UsesNamedCaptureReferences
            ? captureNamesBuffer ?? new Dictionary<string, int>(StringComparer.Ordinal)
            : captureNamesBuffer;
        captureNames?.Clear();
        if (capturePlan is null ||
            template.UsesNamedCaptureReferences ||
            !capturePlan.TryCollectNumericCaptures(matched, captureStarts, captureLengths))
        {
            captureNames ??= new Dictionary<string, int>(StringComparer.Ordinal);
            CollectCaptures(patterns, matched, asciiCaseInsensitive, captureStarts, captureLengths, captureNames);
        }

        template.AddExpanded(bytes, matched, captureStarts, captureLengths, captureNames);
    }

    private static void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> matched,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan,
        ReplacementTemplate? template,
        int[]? captureStartsBuffer,
        int[]? captureLengthsBuffer,
        Dictionary<string, int>? captureNamesBuffer)
    {
        template ??= ReplacementTemplate.Create(replacement, patterns);
        if (capturePlan?.TryWriteExpandedNumericReplacement(output, matched, template) == true)
        {
            return;
        }

        output.Write(Expand(
            replacement,
            matched,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureStartsBuffer,
            captureLengthsBuffer,
            captureNamesBuffer));
    }

    internal static int CountCapturingGroups(ReadOnlySpan<byte> pattern)
    {
        int count = 0;
        int patternIndex = 0;
        while (patternIndex < pattern.Length)
        {
            byte token = pattern[patternIndex];
            if (token == (byte)'\\')
            {
                patternIndex += patternIndex + 1 < pattern.Length ? 2 : 1;
                continue;
            }

            if (token == (byte)'[' && TryFindClassEnd(pattern, patternIndex, out int classEnd))
            {
                patternIndex = classEnd + 1;
                continue;
            }

            if (token == (byte)'(' && TryFindGroupEnd(pattern, patternIndex, out _, out _, out bool capturing, out _, out _, out _, out _))
            {
                if (capturing)
                {
                    count++;
                }

                patternIndex++;
                continue;
            }

            patternIndex++;
        }

        return count;
    }

    private static int[] CreateCaptureArray(int highestCapture)
    {
        int[] values = new int[Math.Max(1, highestCapture + 1)];
        Array.Fill(values, -1);
        return values;
    }

    private static bool TryReadCaptureIndex(ReadOnlySpan<byte> capture, out int captureIndex)
    {
        captureIndex = 0;
        if (capture.IsEmpty)
        {
            return false;
        }

        for (int index = 0; index < capture.Length; index++)
        {
            if (!IsAsciiDigit(capture[index]))
            {
                return false;
            }

            int digit = capture[index] - (byte)'0';
            if (captureIndex > (int.MaxValue - digit) / 10)
            {
                return false;
            }

            captureIndex = (captureIndex * 10) + digit;
        }

        return true;
    }

    private static void AddCapture(
        List<byte> bytes,
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths,
        int captureIndex)
    {
        if (captureIndex >= captureStarts.Length)
        {
            return;
        }

        int start = captureStarts[captureIndex];
        int length = captureLengths[captureIndex];
        if (start < 0 || length < 0 || start + length > matched.Length)
        {
            return;
        }

        Add(bytes, matched.Slice(start, length));
    }

    private static void CollectCaptures(
        IReadOnlyList<byte[]> patterns,
        ReadOnlySpan<byte> matched,
        bool asciiCaseInsensitive,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames)
    {
        captureStarts[0] = 0;
        captureLengths[0] = matched.Length;
        for (int index = 0; index < patterns.Count; index++)
        {
            byte[] pattern = patterns[index];
            Array.Fill(captureStarts, -1, 1, captureStarts.Length - 1);
            Array.Fill(captureLengths, -1, 1, captureLengths.Length - 1);
            captureNames.Clear();
            int captureIndex = 1;
            if (TryMatchCaptureExpression(pattern, matched, captureOffset: 0, asciiCaseInsensitive, ignoreWhitespace: false, dotMatchesNewline: false, captureStarts, captureLengths, captureNames, ref captureIndex, out _, requireFullLength: true))
            {
                return;
            }
        }

        Array.Fill(captureStarts, -1, 1, captureStarts.Length - 1);
        Array.Fill(captureLengths, -1, 1, captureLengths.Length - 1);
        captureNames.Clear();
    }

    private static bool TryMatchCaptureExpression(
        ReadOnlySpan<byte> pattern,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool dotMatchesNewline,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        ref int captureIndex,
        out int end,
        bool requireFullLength = false)
    {
        int baseCaptureIndex = captureIndex;
        int totalCaptures = CountCapturingGroups(pattern);
        int alternativeStart = 0;
        int capturesBeforeAlternative = 0;
        bool alternativeCaseInsensitive = asciiCaseInsensitive;
        bool alternativeIgnoreWhitespace = ignoreWhitespace;
        while (alternativeStart <= pattern.Length)
        {
            int alternativeEnd = FindAlternativeEnd(
                pattern,
                alternativeStart,
                alternativeCaseInsensitive,
                alternativeIgnoreWhitespace,
                dotMatchesNewline,
                out bool nextCaseInsensitive,
                out bool nextIgnoreWhitespace,
                out bool nextDotMatchesNewline);
            int alternativeCaptureIndex = baseCaptureIndex + capturesBeforeAlternative;
            int[] alternativeCaptureStarts = (int[])captureStarts.Clone();
            int[] alternativeCaptureLengths = (int[])captureLengths.Clone();
            var alternativeCaptureNames = new Dictionary<string, int>(captureNames, StringComparer.Ordinal);
            if (TryMatchCaptureSequence(
                    pattern[alternativeStart..alternativeEnd],
                    matched,
                    captureOffset,
                    alternativeCaseInsensitive,
                    alternativeIgnoreWhitespace,
                    dotMatchesNewline,
                    alternativeCaptureStarts,
                    alternativeCaptureLengths,
                    alternativeCaptureNames,
                    ref alternativeCaptureIndex,
                    out end,
                    requireFullLength))
            {
                Array.Copy(alternativeCaptureStarts, captureStarts, captureStarts.Length);
                Array.Copy(alternativeCaptureLengths, captureLengths, captureLengths.Length);
                captureNames.Clear();
                foreach (KeyValuePair<string, int> captureName in alternativeCaptureNames)
                {
                    captureNames[captureName.Key] = captureName.Value;
                }

                captureIndex = baseCaptureIndex + totalCaptures;
                return true;
            }

            capturesBeforeAlternative += CountCapturingGroups(pattern[alternativeStart..alternativeEnd]);
            if (alternativeEnd == pattern.Length)
            {
                break;
            }

            alternativeStart = alternativeEnd + 1;
            alternativeCaseInsensitive = nextCaseInsensitive;
            alternativeIgnoreWhitespace = nextIgnoreWhitespace;
            dotMatchesNewline = nextDotMatchesNewline;
        }

        captureIndex = baseCaptureIndex;
        end = 0;
        return false;
    }

    private static bool TryMatchCaptureSequence(
        ReadOnlySpan<byte> pattern,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool dotMatchesNewline,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        ref int captureIndex,
        out int end,
        bool requireFullLength = false)
    {
        return TryMatchCaptureSequenceFrom(
            pattern,
            patternIndex: 0,
            matched,
            captureOffset,
            matchedIndex: 0,
            asciiCaseInsensitive,
            ignoreWhitespace,
            dotMatchesNewline,
            captureStarts,
            captureLengths,
            captureNames,
            ref captureIndex,
            out end,
            requireFullLength);
    }

    private static bool TryMatchCaptureSequenceFrom(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        int matchedIndex,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool dotMatchesNewline,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        ref int captureIndex,
        out int end,
        bool requireFullLength)
    {
        while (true)
        {
            SkipIgnoredCapturePatternBytes(pattern, ref patternIndex, ignoreWhitespace);
            if (patternIndex >= pattern.Length)
            {
                if (requireFullLength && matchedIndex != matched.Length)
                {
                    end = 0;
                    return false;
                }

                break;
            }

            if (TryReadInlineCaptureFlag(pattern, patternIndex, out bool? caseOverride, out bool? ignoreWhitespaceOverride, out bool? dotOverride, out int nextPatternIndex))
            {
                asciiCaseInsensitive = caseOverride ?? asciiCaseInsensitive;
                ignoreWhitespace = ignoreWhitespaceOverride ?? ignoreWhitespace;
                dotMatchesNewline = dotOverride ?? dotMatchesNewline;
                patternIndex = nextPatternIndex;
                continue;
            }

            return TryMatchCaptureAtomThen(
                pattern,
                patternIndex,
                matched,
                captureOffset,
                matchedIndex,
                asciiCaseInsensitive,
                ignoreWhitespace,
                dotMatchesNewline,
                captureStarts,
                captureLengths,
                captureNames,
                ref captureIndex,
                out end,
                requireFullLength);
        }

        end = matchedIndex;
        return true;
    }

    private static bool TryMatchCaptureAtomThen(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        int matchedIndex,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool dotMatchesNewline,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        ref int captureIndex,
        out int end,
        bool requireFullLength)
    {
        if (patternIndex >= pattern.Length)
        {
            end = 0;
            return false;
        }

        byte token = pattern[patternIndex];
        if (token == (byte)'^')
        {
            if (matchedIndex != 0 && matched[matchedIndex - 1] != (byte)'\n')
            {
                end = 0;
                return false;
            }

            return TryMatchCaptureSequenceFrom(pattern, patternIndex + 1, matched, captureOffset, matchedIndex, asciiCaseInsensitive, ignoreWhitespace, dotMatchesNewline, captureStarts, captureLengths, captureNames, ref captureIndex, out end, requireFullLength);
        }

        if (token == (byte)'$')
        {
            if (matchedIndex != matched.Length && matched[matchedIndex] != (byte)'\n')
            {
                end = 0;
                return false;
            }

            return TryMatchCaptureSequenceFrom(pattern, patternIndex + 1, matched, captureOffset, matchedIndex, asciiCaseInsensitive, ignoreWhitespace, dotMatchesNewline, captureStarts, captureLengths, captureNames, ref captureIndex, out end, requireFullLength);
        }

        if (token == (byte)'(' && TryFindGroupEnd(pattern, patternIndex, out int contentStart, out int groupEnd, out bool capturing, out string? captureName, out bool? groupCaseOverride, out bool? groupIgnoreWhitespaceOverride, out bool? groupDotOverride))
        {
            int assignedCapture = capturing ? captureIndex : -1;
            int groupContentCaptures = CountCapturingGroups(pattern[contentStart..groupEnd]);
            int afterGroupCaptureIndex = captureIndex + groupContentCaptures + (capturing ? 1 : 0);
            bool groupCaseInsensitive = groupCaseOverride ?? asciiCaseInsensitive;
            bool groupIgnoreWhitespace = groupIgnoreWhitespaceOverride ?? ignoreWhitespace;
            bool groupDotMatchesNewline = groupDotOverride ?? dotMatchesNewline;
            int suffixPatternIndex = groupEnd + 1;
            SkipIgnoredCapturePatternBytes(pattern, ref suffixPatternIndex, ignoreWhitespace);
            int quantifierStartIndex = suffixPatternIndex;
            ParseCaptureQuantifier(pattern, ref suffixPatternIndex, out int groupMin, out int groupMax, out bool groupLazy);
            if (groupMin == 1 && groupMax == 1 && !groupLazy && suffixPatternIndex == quantifierStartIndex)
            {
                return TryMatchSingleCaptureGroupThen(
                    pattern,
                    contentStart,
                    groupEnd,
                    suffixPatternIndex,
                    matched,
                    captureOffset,
                    matchedIndex,
                    groupCaseInsensitive,
                    groupIgnoreWhitespace,
                    groupDotMatchesNewline,
                    asciiCaseInsensitive,
                    ignoreWhitespace,
                    dotMatchesNewline,
                    assignedCapture,
                    captureName,
                    afterGroupCaptureIndex,
                    captureStarts,
                    captureLengths,
                    captureNames,
                    ref captureIndex,
                    out end,
                    requireFullLength);
            }

            var states = new List<(int MatchedIndex, int[] Starts, int[] Lengths, Dictionary<string, int> Names)>
            {
                (matchedIndex, (int[])captureStarts.Clone(), (int[])captureLengths.Clone(), new Dictionary<string, int>(captureNames, StringComparer.Ordinal)),
            };
            int currentMatchedIndex = matchedIndex;
            int[] currentStarts = (int[])captureStarts.Clone();
            int[] currentLengths = (int[])captureLengths.Clone();
            var currentNames = new Dictionary<string, int>(captureNames, StringComparer.Ordinal);
            int maxMatches = Math.Min(groupMax, matched.Length - matchedIndex);
            for (int groupCount = 0; groupCount < maxMatches; groupCount++)
            {
                int innerCapture = captureIndex + (capturing ? 1 : 0);
                int groupStart = currentMatchedIndex;
                if (!TryMatchCaptureExpression(
                        pattern[contentStart..groupEnd],
                        matched[currentMatchedIndex..],
                        captureOffset + currentMatchedIndex,
                        groupCaseInsensitive,
                        groupIgnoreWhitespace,
                        groupDotMatchesNewline,
                        currentStarts,
                        currentLengths,
                        currentNames,
                        ref innerCapture,
                        out int groupLength))
                {
                    break;
                }

                if (assignedCapture >= 0 && assignedCapture < currentStarts.Length)
                {
                    currentStarts[assignedCapture] = captureOffset + groupStart;
                    currentLengths[assignedCapture] = groupLength;
                }

                if (assignedCapture >= 0 && captureName is not null)
                {
                    currentNames[captureName] = assignedCapture;
                }

                currentMatchedIndex += groupLength;
                states.Add((currentMatchedIndex, (int[])currentStarts.Clone(), (int[])currentLengths.Clone(), new Dictionary<string, int>(currentNames, StringComparer.Ordinal)));
                if (groupLength == 0)
                {
                    break;
                }
            }

            return TryMatchCaptureRepetitionStatesThen(
                pattern,
                suffixPatternIndex,
                matched,
                captureOffset,
                asciiCaseInsensitive,
                ignoreWhitespace,
                dotMatchesNewline,
                captureStarts,
                captureLengths,
                captureNames,
                states,
                groupMin,
                groupLazy,
                afterGroupCaptureIndex,
                ref captureIndex,
                out end,
                requireFullLength);
        }

        byte literal = 0;
        int classStart = -1;
        int classEnd = -1;
        int atomKind;
        if (token == (byte)'[' && TryFindClassEnd(pattern, patternIndex, out classEnd))
        {
            atomKind = CaptureAtomClass;
            classStart = patternIndex + 1;
            patternIndex = classEnd + 1;
        }
        else if (token == (byte)'.')
        {
            atomKind = CaptureAtomDot;
            patternIndex++;
        }
        else if (token == (byte)'\\' && patternIndex + 1 < pattern.Length)
        {
            byte escaped = pattern[patternIndex + 1];
            atomKind = escaped switch
            {
                (byte)'d' => CaptureAtomDigit,
                (byte)'D' => CaptureAtomNotDigit,
                (byte)'w' => CaptureAtomWord,
                (byte)'W' => CaptureAtomNotWord,
                (byte)'s' => CaptureAtomWhitespace,
                (byte)'S' => CaptureAtomNotWhitespace,
                _ => CaptureAtomLiteral,
            };
            literal = escaped switch
            {
                (byte)'n' => (byte)'\n',
                (byte)'t' => (byte)'\t',
                (byte)'r' => (byte)'\r',
                (byte)'f' => (byte)'\f',
                _ => escaped,
            };
            patternIndex += 2;
        }
        else
        {
            atomKind = CaptureAtomLiteral;
            literal = token;
            patternIndex++;
        }

        ReadOnlySpan<byte> expression = classStart >= 0 ? pattern[classStart..classEnd] : [];
        SkipIgnoredCapturePatternBytes(pattern, ref patternIndex, ignoreWhitespace);
        ParseCaptureQuantifier(pattern, ref patternIndex, out int min, out int max, out bool atomLazy);
        int count = 0;
        while (count < max &&
            matchedIndex < matched.Length &&
            CaptureAtomMatches(matched[matchedIndex], atomKind, literal, expression, asciiCaseInsensitive, dotMatchesNewline))
        {
            matchedIndex++;
            count++;
        }

        if (count < min)
        {
            end = 0;
            return false;
        }

        return TryMatchCaptureAtomCountsThen(
            pattern,
            patternIndex,
            matched,
            captureOffset,
            matchedIndex - count,
            count,
            min,
            atomLazy,
            asciiCaseInsensitive,
            ignoreWhitespace,
            dotMatchesNewline,
            captureStarts,
            captureLengths,
            captureNames,
            ref captureIndex,
            out end,
            requireFullLength);
    }

    private static bool TryMatchSingleCaptureGroupThen(
        ReadOnlySpan<byte> pattern,
        int contentStart,
        int groupEnd,
        int suffixPatternIndex,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        int matchedIndex,
        bool groupCaseInsensitive,
        bool groupIgnoreWhitespace,
        bool groupDotMatchesNewline,
        bool suffixCaseInsensitive,
        bool suffixIgnoreWhitespace,
        bool suffixDotMatchesNewline,
        int assignedCapture,
        string? captureName,
        int nextCaptureIndex,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        ref int captureIndex,
        out int end,
        bool requireFullLength)
    {
        int maxLength = matched.Length - matchedIndex;
        if (!TryBuildSingleGroupState(
                pattern,
                contentStart,
                groupEnd,
                matched,
                captureOffset,
                matchedIndex,
                maxLength,
                groupCaseInsensitive,
                groupIgnoreWhitespace,
                groupDotMatchesNewline,
                assignedCapture,
                captureName,
                captureIndex,
                captureStarts,
                captureLengths,
                captureNames,
                requireFullLength: false,
                out int preferredLength,
                out int[] preferredStarts,
                out int[] preferredLengths,
                out Dictionary<string, int> preferredNames))
        {
            end = 0;
            return false;
        }

        if (TryMatchSingleCaptureGroupStateThen(
                pattern,
                suffixPatternIndex,
                matched,
                captureOffset,
                matchedIndex,
                preferredLength,
                suffixCaseInsensitive,
                suffixIgnoreWhitespace,
                suffixDotMatchesNewline,
                nextCaptureIndex,
                preferredStarts,
                preferredLengths,
                preferredNames,
                captureStarts,
                captureLengths,
                captureNames,
                ref captureIndex,
                out end,
                requireFullLength))
        {
            return true;
        }

        for (int candidateLength = preferredLength + 1; candidateLength <= maxLength; candidateLength++)
        {
            if (TryBuildSingleGroupState(
                    pattern,
                    contentStart,
                    groupEnd,
                    matched,
                    captureOffset,
                    matchedIndex,
                    candidateLength,
                    groupCaseInsensitive,
                    groupIgnoreWhitespace,
                    groupDotMatchesNewline,
                    assignedCapture,
                    captureName,
                    captureIndex,
                    captureStarts,
                    captureLengths,
                    captureNames,
                    requireFullLength: true,
                    out _,
                    out int[] candidateStarts,
                    out int[] candidateLengths,
                    out Dictionary<string, int> candidateNames) &&
                TryMatchSingleCaptureGroupStateThen(
                    pattern,
                    suffixPatternIndex,
                    matched,
                    captureOffset,
                    matchedIndex,
                    candidateLength,
                    suffixCaseInsensitive,
                    suffixIgnoreWhitespace,
                    suffixDotMatchesNewline,
                    nextCaptureIndex,
                    candidateStarts,
                    candidateLengths,
                    candidateNames,
                    captureStarts,
                    captureLengths,
                    captureNames,
                    ref captureIndex,
                    out end,
                    requireFullLength))
            {
                return true;
            }
        }

        for (int candidateLength = preferredLength - 1; candidateLength >= 0; candidateLength--)
        {
            if (TryBuildSingleGroupState(
                    pattern,
                    contentStart,
                    groupEnd,
                    matched,
                    captureOffset,
                    matchedIndex,
                    candidateLength,
                    groupCaseInsensitive,
                    groupIgnoreWhitespace,
                    groupDotMatchesNewline,
                    assignedCapture,
                    captureName,
                    captureIndex,
                    captureStarts,
                    captureLengths,
                    captureNames,
                    requireFullLength: true,
                    out _,
                    out int[] candidateStarts,
                    out int[] candidateLengths,
                    out Dictionary<string, int> candidateNames) &&
                TryMatchSingleCaptureGroupStateThen(
                    pattern,
                    suffixPatternIndex,
                    matched,
                    captureOffset,
                    matchedIndex,
                    candidateLength,
                    suffixCaseInsensitive,
                    suffixIgnoreWhitespace,
                    suffixDotMatchesNewline,
                    nextCaptureIndex,
                    candidateStarts,
                    candidateLengths,
                    candidateNames,
                    captureStarts,
                    captureLengths,
                    captureNames,
                    ref captureIndex,
                    out end,
                    requireFullLength))
            {
                return true;
            }
        }

        end = 0;
        return false;
    }

    private static bool TryBuildSingleGroupState(
        ReadOnlySpan<byte> pattern,
        int contentStart,
        int groupEnd,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        int matchedIndex,
        int candidateLength,
        bool groupCaseInsensitive,
        bool groupIgnoreWhitespace,
        bool groupDotMatchesNewline,
        int assignedCapture,
        string? captureName,
        int captureIndex,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        bool requireFullLength,
        out int groupLength,
        out int[] candidateStarts,
        out int[] candidateLengths,
        out Dictionary<string, int> candidateNames)
    {
        candidateStarts = (int[])captureStarts.Clone();
        candidateLengths = (int[])captureLengths.Clone();
        candidateNames = new Dictionary<string, int>(captureNames, StringComparer.Ordinal);
        int innerCapture = captureIndex + (assignedCapture >= 0 ? 1 : 0);
        if (!TryMatchCaptureExpression(
                pattern[contentStart..groupEnd],
                matched.Slice(matchedIndex, candidateLength),
                captureOffset + matchedIndex,
                groupCaseInsensitive,
                groupIgnoreWhitespace,
                groupDotMatchesNewline,
                candidateStarts,
                candidateLengths,
                candidateNames,
                ref innerCapture,
                out groupLength,
                requireFullLength) ||
            (requireFullLength && groupLength != candidateLength))
        {
            return false;
        }

        if (assignedCapture >= 0 && assignedCapture < candidateStarts.Length)
        {
            candidateStarts[assignedCapture] = captureOffset + matchedIndex;
            candidateLengths[assignedCapture] = groupLength;
        }

        if (assignedCapture >= 0 && captureName is not null)
        {
            candidateNames[captureName] = assignedCapture;
        }

        return true;
    }

    private static bool TryMatchSingleCaptureGroupStateThen(
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        int matchedIndex,
        int groupLength,
        bool suffixCaseInsensitive,
        bool suffixIgnoreWhitespace,
        bool suffixDotMatchesNewline,
        int nextCaptureIndex,
        int[] stateStarts,
        int[] stateLengths,
        Dictionary<string, int> stateNames,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        ref int captureIndex,
        out int end,
        bool requireFullLength)
    {
        int suffixCaptureIndex = nextCaptureIndex;
        if (TryMatchCaptureSequenceFrom(
                pattern,
                suffixPatternIndex,
                matched,
                captureOffset,
                matchedIndex + groupLength,
                suffixCaseInsensitive,
                suffixIgnoreWhitespace,
                suffixDotMatchesNewline,
                stateStarts,
                stateLengths,
                stateNames,
                ref suffixCaptureIndex,
                out end,
                requireFullLength))
        {
            CopyCaptureState(stateStarts, stateLengths, stateNames, captureStarts, captureLengths, captureNames);
            captureIndex = suffixCaptureIndex;
            return true;
        }

        return false;
    }

    private static bool TryMatchCaptureRepetitionStatesThen(
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool dotMatchesNewline,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        List<(int MatchedIndex, int[] Starts, int[] Lengths, Dictionary<string, int> Names)> states,
        int min,
        bool lazy,
        int nextCaptureIndex,
        ref int captureIndex,
        out int end,
        bool requireFullLength)
    {
        int start = lazy ? min : states.Count - 1;
        int stop = lazy ? states.Count : min - 1;
        int step = lazy ? 1 : -1;
        for (int count = start; count != stop; count += step)
        {
            if (count < 0 || count >= states.Count)
            {
                continue;
            }

            (int stateMatchedIndex, int[] stateStarts, int[] stateLengths, Dictionary<string, int> stateNames) = states[count];
            int[] suffixStarts = (int[])stateStarts.Clone();
            int[] suffixLengths = (int[])stateLengths.Clone();
            var suffixNames = new Dictionary<string, int>(stateNames, StringComparer.Ordinal);
            int suffixCaptureIndex = nextCaptureIndex;
            if (TryMatchCaptureSequenceFrom(
                    pattern,
                    suffixPatternIndex,
                    matched,
                    captureOffset,
                    stateMatchedIndex,
                    asciiCaseInsensitive,
                    ignoreWhitespace,
                    dotMatchesNewline,
                    suffixStarts,
                    suffixLengths,
                    suffixNames,
                    ref suffixCaptureIndex,
                    out end,
                    requireFullLength))
            {
                CopyCaptureState(suffixStarts, suffixLengths, suffixNames, captureStarts, captureLengths, captureNames);
                captureIndex = suffixCaptureIndex;
                return true;
            }
        }

        end = 0;
        return false;
    }

    private static bool TryMatchCaptureAtomCountsThen(
        ReadOnlySpan<byte> pattern,
        int suffixPatternIndex,
        ReadOnlySpan<byte> matched,
        int captureOffset,
        int matchedIndex,
        int maxCount,
        int min,
        bool lazy,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool dotMatchesNewline,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int> captureNames,
        ref int captureIndex,
        out int end,
        bool requireFullLength)
    {
        int start = lazy ? min : maxCount;
        int stop = lazy ? maxCount + 1 : min - 1;
        int step = lazy ? 1 : -1;
        for (int count = start; count != stop; count += step)
        {
            int[] suffixStarts = (int[])captureStarts.Clone();
            int[] suffixLengths = (int[])captureLengths.Clone();
            var suffixNames = new Dictionary<string, int>(captureNames, StringComparer.Ordinal);
            int suffixCaptureIndex = captureIndex;
            if (TryMatchCaptureSequenceFrom(
                    pattern,
                    suffixPatternIndex,
                    matched,
                    captureOffset,
                    matchedIndex + count,
                    asciiCaseInsensitive,
                    ignoreWhitespace,
                    dotMatchesNewline,
                    suffixStarts,
                    suffixLengths,
                    suffixNames,
                    ref suffixCaptureIndex,
                    out end,
                    requireFullLength))
            {
                CopyCaptureState(suffixStarts, suffixLengths, suffixNames, captureStarts, captureLengths, captureNames);
                captureIndex = suffixCaptureIndex;
                return true;
            }
        }

        end = 0;
        return false;
    }

    private static void CopyCaptureState(
        int[] sourceStarts,
        int[] sourceLengths,
        Dictionary<string, int> sourceNames,
        int[] destinationStarts,
        int[] destinationLengths,
        Dictionary<string, int> destinationNames)
    {
        Array.Copy(sourceStarts, destinationStarts, destinationStarts.Length);
        Array.Copy(sourceLengths, destinationLengths, destinationLengths.Length);
        destinationNames.Clear();
        foreach (KeyValuePair<string, int> captureName in sourceNames)
        {
            destinationNames[captureName.Key] = captureName.Value;
        }
    }

    private static int FindAlternativeEnd(
        ReadOnlySpan<byte> pattern,
        int start,
        bool asciiCaseInsensitive,
        bool ignoreWhitespace,
        bool dotMatchesNewline,
        out bool nextCaseInsensitive,
        out bool nextIgnoreWhitespace,
        out bool nextDotMatchesNewline)
    {
        int classDepth = 0;
        int groupDepth = 0;
        nextCaseInsensitive = asciiCaseInsensitive;
        nextIgnoreWhitespace = ignoreWhitespace;
        nextDotMatchesNewline = dotMatchesNewline;
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

                if (TryReadInlineCaptureFlag(pattern, index, out bool? caseOverride, out bool? ignoreWhitespaceOverride, out bool? dotOverride, out int nextPatternIndex))
                {
                    nextCaseInsensitive = caseOverride ?? nextCaseInsensitive;
                    nextIgnoreWhitespace = ignoreWhitespaceOverride ?? nextIgnoreWhitespace;
                    nextDotMatchesNewline = dotOverride ?? nextDotMatchesNewline;
                    index = nextPatternIndex - 1;
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

    private static void SkipIgnoredCapturePatternBytes(ReadOnlySpan<byte> pattern, ref int patternIndex, bool ignoreWhitespace)
    {
        if (!ignoreWhitespace)
        {
            return;
        }

        while (patternIndex < pattern.Length)
        {
            if (IsCaptureIgnoredWhitespaceByte(pattern[patternIndex]))
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

    private static bool TryReadInlineCaptureFlag(
        ReadOnlySpan<byte> pattern,
        int patternIndex,
        out bool? caseOverride,
        out bool? ignoreWhitespaceOverride,
        out bool? dotOverride,
        out int nextPatternIndex)
    {
        caseOverride = null;
        ignoreWhitespaceOverride = null;
        dotOverride = null;
        nextPatternIndex = patternIndex;
        if (patternIndex + 3 > pattern.Length ||
            pattern[patternIndex] != (byte)'(' ||
            pattern[patternIndex + 1] != (byte)'?')
        {
            return false;
        }

        bool disabling = false;
        bool sawFlag = false;
        int index = patternIndex + 2;
        while (index < pattern.Length && pattern[index] != (byte)')')
        {
            byte value = pattern[index];
            if (value == (byte)':')
            {
                return false;
            }

            if (value == (byte)'-')
            {
                if (disabling)
                {
                    return false;
                }

                disabling = true;
                index++;
                continue;
            }

            if (!ApplyCaptureFlag(value, disabling, ref caseOverride, ref ignoreWhitespaceOverride, ref dotOverride))
            {
                return false;
            }

            sawFlag = true;
            index++;
        }

        if (!sawFlag || index >= pattern.Length || pattern[index] != (byte)')')
        {
            return false;
        }

        nextPatternIndex = index + 1;
        return true;
    }

    private static bool ApplyCaptureFlag(byte flag, bool disabling, ref bool? caseOverride, ref bool? ignoreWhitespaceOverride, ref bool? dotOverride)
    {
        switch (flag)
        {
            case (byte)'i':
                caseOverride = !disabling;
                return true;
            case (byte)'x':
                ignoreWhitespaceOverride = !disabling;
                return true;
            case (byte)'s':
                dotOverride = !disabling;
                return true;
            case (byte)'m':
            case (byte)'U':
                return true;
            default:
                return false;
        }
    }

    private static void ParseCaptureQuantifier(ReadOnlySpan<byte> pattern, ref int patternIndex, out int min, out int max, out bool lazy)
    {
        min = 1;
        max = 1;
        lazy = false;
        if (patternIndex >= pattern.Length)
        {
            return;
        }

        byte token = pattern[patternIndex];
        if (token == (byte)'?')
        {
            min = 0;
            max = 1;
            patternIndex++;
            lazy = ConsumeLazySuffix(pattern, ref patternIndex);
            return;
        }

        if (token == (byte)'*')
        {
            min = 0;
            max = int.MaxValue;
            patternIndex++;
            lazy = ConsumeLazySuffix(pattern, ref patternIndex);
            return;
        }

        if (token == (byte)'+')
        {
            min = 1;
            max = int.MaxValue;
            patternIndex++;
            lazy = ConsumeLazySuffix(pattern, ref patternIndex);
            return;
        }

        if (token != (byte)'{')
        {
            return;
        }

        int index = patternIndex + 1;
        if (!TryReadDecimal(pattern, ref index, out int parsedMin))
        {
            return;
        }

        int parsedMax = parsedMin;
        if (index < pattern.Length && pattern[index] == (byte)',')
        {
            index++;
            parsedMax = int.MaxValue;
            if (index < pattern.Length && IsAsciiDigit(pattern[index]) &&
                !TryReadDecimal(pattern, ref index, out parsedMax))
            {
                return;
            }
        }

        if (index >= pattern.Length || pattern[index] != (byte)'}' || parsedMax < parsedMin)
        {
            return;
        }

        min = parsedMin;
        max = parsedMax;
        patternIndex = index + 1;
        lazy = ConsumeLazySuffix(pattern, ref patternIndex);
    }

    private static bool ConsumeLazySuffix(ReadOnlySpan<byte> pattern, ref int patternIndex)
    {
        if (patternIndex < pattern.Length && pattern[patternIndex] == (byte)'?')
        {
            patternIndex++;
            return true;
        }

        return false;
    }

    private static bool TryReadDecimal(ReadOnlySpan<byte> pattern, ref int index, out int value)
    {
        value = 0;
        int start = index;
        while (index < pattern.Length && IsAsciiDigit(pattern[index]))
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

    private static bool CaptureAtomMatches(
        byte value,
        int atomKind,
        byte literal,
        ReadOnlySpan<byte> expression,
        bool asciiCaseInsensitive,
        bool dotMatchesNewline)
    {
        return atomKind switch
        {
            CaptureAtomClass => CaptureClassMatches(value, expression, asciiCaseInsensitive),
            CaptureAtomDot => dotMatchesNewline || value != (byte)'\n',
            CaptureAtomDigit => IsAsciiDigit(value),
            CaptureAtomNotDigit => value != (byte)'\n' && !IsAsciiDigit(value),
            CaptureAtomWord => IsAsciiWordByte(value),
            CaptureAtomNotWord => value != (byte)'\n' && !IsAsciiWordByte(value),
            CaptureAtomWhitespace => IsRegexWhitespaceByte(value),
            CaptureAtomNotWhitespace => !IsRegexWhitespaceByte(value),
            _ => ByteEquals(value, literal, asciiCaseInsensitive),
        };
    }

    private static bool CaptureClassMatches(byte value, ReadOnlySpan<byte> expression, bool asciiCaseInsensitive)
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
            if (!TryReadCaptureClassToken(expression, ref index, out int tokenKind, out byte literal, out bool tokenNegated))
            {
                break;
            }

            if (!tokenNegated &&
                tokenKind == CaptureAtomLiteral &&
                index + 1 < expression.Length &&
                expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (!TryReadCaptureClassToken(expression, ref rangeEndIndex, out int rangeEndKind, out byte rangeEndLiteral, out bool rangeEndNegated) ||
                    rangeEndNegated ||
                    rangeEndKind != CaptureAtomLiteral)
                {
                    if (CaptureClassTokenMatches(value, tokenKind, literal, tokenNegated, asciiCaseInsensitive))
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

            if (CaptureClassTokenMatches(value, tokenKind, literal, tokenNegated, asciiCaseInsensitive))
            {
                matched = true;
            }
        }

        return negated ? !matched : matched;
    }

    private static bool TryReadCaptureClassToken(
        ReadOnlySpan<byte> expression,
        ref int index,
        out int tokenKind,
        out byte literal,
        out bool tokenNegated)
    {
        tokenKind = CaptureAtomLiteral;
        literal = 0;
        tokenNegated = false;
        if (index >= expression.Length)
        {
            return false;
        }

        if (TryParseCapturePosixClass(expression, index, out tokenKind, out tokenNegated, out int nextIndex))
        {
            index = nextIndex;
            return true;
        }

        if (expression[index] == (byte)'\\' && index + 1 < expression.Length)
        {
            byte escaped = expression[index + 1];
            switch (escaped)
            {
                case (byte)'d':
                    tokenKind = CaptureAtomDigit;
                    index += 2;
                    return true;
                case (byte)'D':
                    tokenKind = CaptureAtomDigit;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'w':
                    tokenKind = CaptureAtomWord;
                    index += 2;
                    return true;
                case (byte)'W':
                    tokenKind = CaptureAtomWord;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'s':
                    tokenKind = CaptureAtomWhitespace;
                    index += 2;
                    return true;
                case (byte)'S':
                    tokenKind = CaptureAtomWhitespace;
                    tokenNegated = true;
                    index += 2;
                    return true;
                case (byte)'n':
                    literal = (byte)'\n';
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

    private static bool TryParseCapturePosixClass(
        ReadOnlySpan<byte> expression,
        int index,
        out int tokenKind,
        out bool tokenNegated,
        out int nextIndex)
    {
        tokenKind = CaptureAtomLiteral;
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

        if (!TryGetCapturePosixClassKind(expression[nameStart..nameEnd], out tokenKind))
        {
            return false;
        }

        nextIndex = nameEnd + 2;
        return true;
    }

    private static bool TryGetCapturePosixClassKind(ReadOnlySpan<byte> name, out int tokenKind)
    {
        tokenKind = CaptureAtomLiteral;
        if (name.SequenceEqual("alnum"u8))
        {
            tokenKind = CaptureClassAlnum;
            return true;
        }

        if (name.SequenceEqual("alpha"u8))
        {
            tokenKind = CaptureClassAlpha;
            return true;
        }

        if (name.SequenceEqual("ascii"u8))
        {
            tokenKind = CaptureClassAscii;
            return true;
        }

        if (name.SequenceEqual("blank"u8))
        {
            tokenKind = CaptureClassBlank;
            return true;
        }

        if (name.SequenceEqual("cntrl"u8))
        {
            tokenKind = CaptureClassControl;
            return true;
        }

        if (name.SequenceEqual("digit"u8))
        {
            tokenKind = CaptureAtomDigit;
            return true;
        }

        if (name.SequenceEqual("graph"u8))
        {
            tokenKind = CaptureClassGraph;
            return true;
        }

        if (name.SequenceEqual("lower"u8))
        {
            tokenKind = CaptureClassLower;
            return true;
        }

        if (name.SequenceEqual("print"u8))
        {
            tokenKind = CaptureClassPrint;
            return true;
        }

        if (name.SequenceEqual("punct"u8))
        {
            tokenKind = CaptureClassPunct;
            return true;
        }

        if (name.SequenceEqual("space"u8))
        {
            tokenKind = CaptureAtomWhitespace;
            return true;
        }

        if (name.SequenceEqual("upper"u8))
        {
            tokenKind = CaptureClassUpper;
            return true;
        }

        if (name.SequenceEqual("word"u8))
        {
            tokenKind = CaptureAtomWord;
            return true;
        }

        if (name.SequenceEqual("xdigit"u8))
        {
            tokenKind = CaptureClassHexDigit;
            return true;
        }

        return false;
    }

    private static bool CaptureClassTokenMatches(
        byte value,
        int tokenKind,
        byte literal,
        bool tokenNegated,
        bool asciiCaseInsensitive)
    {
        bool matched = tokenKind switch
        {
            CaptureAtomDigit => IsAsciiDigit(value),
            CaptureAtomWord => IsAsciiWordByte(value),
            CaptureAtomWhitespace => IsRegexWhitespaceByte(value),
            CaptureClassAlnum => IsAsciiAlphaByte(value) || IsAsciiDigit(value),
            CaptureClassAlpha => IsAsciiAlphaByte(value),
            CaptureClassAscii => value <= 0x7f,
            CaptureClassBlank => value is (byte)' ' or (byte)'\t',
            CaptureClassControl => value < 0x20 || value == 0x7f,
            CaptureClassGraph => value is >= 0x21 and <= 0x7e,
            CaptureClassLower => asciiCaseInsensitive
                ? IsAsciiAlphaByte(value)
                : value is >= (byte)'a' and <= (byte)'z',
            CaptureClassPrint => value is >= 0x20 and <= 0x7e,
            CaptureClassPunct => (value is >= 0x21 and <= 0x7e) && !IsAsciiAlphaByte(value) && !IsAsciiDigit(value),
            CaptureClassUpper => asciiCaseInsensitive
                ? IsAsciiAlphaByte(value)
                : value is >= (byte)'A' and <= (byte)'Z',
            CaptureClassHexDigit => IsAsciiHexDigitByte(value),
            _ => ByteEquals(value, literal, asciiCaseInsensitive),
        };
        return tokenNegated ? !matched : matched;
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
        out bool capturing,
        out string? captureName,
        out bool? caseOverride,
        out bool? ignoreWhitespaceOverride,
        out bool? dotOverride)
    {
        contentStart = groupStart + 1;
        groupEnd = -1;
        capturing = true;
        captureName = null;
        caseOverride = null;
        ignoreWhitespaceOverride = null;
        dotOverride = null;
        if (contentStart < pattern.Length && pattern[contentStart] == (byte)'?')
        {
            capturing = false;
            if (contentStart + 1 < pattern.Length && pattern[contentStart + 1] == (byte)':')
            {
                contentStart += 2;
            }
            else if (TryParseNamedGroupPrefix(pattern, contentStart, out int namedContentStart, out captureName))
            {
                capturing = true;
                contentStart = namedContentStart;
            }
            else if (TryParseScopedGroupFlags(pattern, contentStart, out int scopedContentStart, out caseOverride, out ignoreWhitespaceOverride, out dotOverride))
            {
                contentStart = scopedContentStart;
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

    private static bool TryParseScopedGroupFlags(
        ReadOnlySpan<byte> pattern,
        int questionIndex,
        out int contentStart,
        out bool? caseOverride,
        out bool? ignoreWhitespaceOverride,
        out bool? dotOverride)
    {
        contentStart = 0;
        caseOverride = null;
        ignoreWhitespaceOverride = null;
        dotOverride = null;
        bool disabling = false;
        bool sawFlag = false;
        int index = questionIndex + 1;
        while (index < pattern.Length)
        {
            byte value = pattern[index];
            if (value == (byte)':')
            {
                if (!sawFlag)
                {
                    return false;
                }

                contentStart = index + 1;
                return true;
            }

            if (value == (byte)'-')
            {
                if (disabling)
                {
                    return false;
                }

                disabling = true;
                index++;
                continue;
            }

            if (!ApplyCaptureFlag(value, disabling, ref caseOverride, ref ignoreWhitespaceOverride, ref dotOverride))
            {
                return false;
            }

            sawFlag = true;
            index++;
        }

        return false;
    }

    private static bool TryParseNamedGroupPrefix(ReadOnlySpan<byte> pattern, int questionIndex, out int contentStart, out string? captureName)
    {
        contentStart = 0;
        captureName = null;
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

        captureName = Encoding.ASCII.GetString(pattern[nameStart..nameEnd]);
        contentStart = nameEnd + 1;
        return true;
    }

    private static bool IsCaptureName(ReadOnlySpan<byte> capture)
    {
        if (capture.IsEmpty || !IsCaptureNameStartByte(capture[0]))
        {
            return false;
        }

        for (int index = 1; index < capture.Length; index++)
        {
            if (!IsCaptureNameByte(capture[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ByteEquals(byte left, byte right, bool asciiCaseInsensitive)
    {
        return FoldMaybe(left, asciiCaseInsensitive) == FoldMaybe(right, asciiCaseInsensitive);
    }

    private static byte FoldMaybe(byte value, bool asciiCaseInsensitive)
    {
        return asciiCaseInsensitive ? FoldAscii(value) : value;
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }

    private static bool IsAsciiDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsAsciiAlphaByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    private static bool IsAsciiHexDigitByte(byte value)
    {
        return IsAsciiDigit(value) ||
            value is >= (byte)'A' and <= (byte)'F'
                or >= (byte)'a' and <= (byte)'f';
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_' ||
            value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z';
    }

    private static bool IsRegexWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static bool IsCaptureIgnoredWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f';
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

    private static void Add(List<byte> bytes, ReadOnlySpan<byte> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            bytes.Add(values[index]);
        }
    }
}
