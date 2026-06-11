namespace Scout;

internal sealed class RegexSimpleSequenceEngine
{
    private const int MinimumLiteralRunLength = 3;
    private const int MaxSegments = 512;
    private const int MaxBoundedRepeat = 1024;
    private const int MaxSelectiveStartBytes = 64;

    private readonly RegexSimpleSequenceSegment[] segments;
    private readonly RegexSimpleSequenceSegment[]? repeatedSegments;
    private readonly int repeatedMinimum;
    private readonly int repeatedMaximum;
    private readonly bool repeatedLazy;

    private RegexSimpleSequenceEngine(List<RegexSimpleSequenceSegment> segments)
    {
        this.segments = [.. segments];
    }

    private RegexSimpleSequenceEngine(List<RegexSimpleSequenceSegment> repeatedSegments, int repeatedMinimum, int repeatedMaximum, bool repeatedLazy)
    {
        segments = [];
        this.repeatedSegments = [.. repeatedSegments];
        this.repeatedMinimum = repeatedMinimum;
        this.repeatedMaximum = repeatedMaximum;
        this.repeatedLazy = repeatedLazy;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexSimpleSequenceEngine? engine)
    {
        engine = null;
        if (options.Utf8 || options.UnicodeClasses)
        {
            return false;
        }

        if (TryCreateRepeatedRoot(root, options, out engine))
        {
            return true;
        }

        var segments = new List<RegexSimpleSequenceSegment>();
        bool sawVariableRepetition = false;
        if (!TryAppend(root, options, segments, ref sawVariableRepetition) ||
            !sawVariableRepetition ||
            segments.Count == 0 ||
            segments.Count > MaxSegments ||
            LongestLiteralRun(segments) < MinimumLiteralRunLength &&
            !HasSelectiveRequiredStart(segments))
        {
            return false;
        }

        engine = new RegexSimpleSequenceEngine(segments);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (repeatedSegments is null && segments.Length == 1)
        {
            return FindSingleSegment(segments[0], haystack, startOffset);
        }

        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (CanStartAt(haystack, start) &&
                TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    public bool TryCountNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, out long count, out long spanSum)
    {
        count = 0;
        spanSum = 0;
        if (TryCountRepeatedCapitalizedWords(haystack, startAt, ref count, ref spanSum))
        {
            return true;
        }

        if (!TryGetSingleRepeatedAtom(out RegexSimpleSequenceSegment segment, out int minimum, out int? maximum, out bool lazy))
        {
            return false;
        }

        int position = Math.Clamp(startAt, 0, haystack.Length);
        if (segment.MatcherKind == RegexSimpleSequenceByteMatcherKind.AsciiLetter)
        {
            CountAsciiLetterRuns(minimum, maximum, lazy, haystack, position, ref count, ref spanSum);
            return true;
        }

        if (segment.MatcherKind == RegexSimpleSequenceByteMatcherKind.AsciiDigit)
        {
            CountAsciiDigitRuns(minimum, maximum, lazy, haystack, position, ref count, ref spanSum);
            return true;
        }

        if (segment.MatcherKind == RegexSimpleSequenceByteMatcherKind.AsciiWord)
        {
            CountAsciiWordRuns(minimum, maximum, lazy, haystack, position, ref count, ref spanSum);
            return true;
        }

        int runLength = 0;
        while (position < haystack.Length)
        {
            if (segment.AtomMatches(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
        return true;
    }

    private bool TryCountRepeatedCapitalizedWords(ReadOnlySpan<byte> haystack, int startAt, ref long count, ref long spanSum)
    {
        if (!IsRepeatedCapitalizedWordPattern(out int wordMinimum, out int wordMaximum, out bool lazy))
        {
            return false;
        }

        int position = Math.Clamp(startAt, 0, haystack.Length);
        int wordsPerMatch = lazy ? wordMinimum : wordMaximum;
        while (position < haystack.Length)
        {
            int runStart = FindNextCapitalizedWordStart(haystack, position);
            if (runStart < 0)
            {
                return true;
            }

            int wordCount = 0;
            int runPosition = runStart;
            while (wordCount < wordsPerMatch &&
                TryConsumeCapitalizedWord(haystack, runPosition, out int nextPosition))
            {
                wordCount++;
                runPosition = nextPosition;
            }

            if (wordCount >= wordMinimum)
            {
                count++;
                spanSum += runPosition - runStart;
                position = runPosition;
                continue;
            }

            position = Math.Max(runPosition, runStart + 1);
        }

        return true;
    }

    private bool IsRepeatedCapitalizedWordPattern(out int minimum, out int maximum, out bool lazy)
    {
        minimum = 0;
        maximum = 0;
        lazy = false;
        if (repeatedSegments is not { Length: 3 } ||
            repeatedMinimum <= 0 ||
            repeatedMaximum < repeatedMinimum ||
            repeatedSegments[0] is not { MatcherKind: RegexSimpleSequenceByteMatcherKind.AsciiUppercase, Minimum: 1, Maximum: 1, Lazy: false } ||
            repeatedSegments[1] is not { MatcherKind: RegexSimpleSequenceByteMatcherKind.AsciiLowercase, Minimum: 1, Maximum: null, Lazy: false } ||
            repeatedSegments[2] is not { MatcherKind: RegexSimpleSequenceByteMatcherKind.RegexWhitespace, Minimum: 0, Maximum: null, Lazy: false })
        {
            return false;
        }

        minimum = repeatedMinimum;
        maximum = repeatedMaximum;
        lazy = repeatedLazy;
        return true;
    }

    private static int FindNextCapitalizedWordStart(ReadOnlySpan<byte> haystack, int position)
    {
        while (position + 1 < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiUppercase(haystack[position]) &&
                RegexSimpleSequenceSegment.IsAsciiLowercase(haystack[position + 1]))
            {
                return position;
            }

            position++;
        }

        return -1;
    }

    private static bool TryConsumeCapitalizedWord(ReadOnlySpan<byte> haystack, int position, out int nextPosition)
    {
        nextPosition = position;
        if (position + 1 >= haystack.Length ||
            !RegexSimpleSequenceSegment.IsAsciiUppercase(haystack[position]) ||
            !RegexSimpleSequenceSegment.IsAsciiLowercase(haystack[position + 1]))
        {
            return false;
        }

        position += 2;
        while (position < haystack.Length &&
            RegexSimpleSequenceSegment.IsAsciiLowercase(haystack[position]))
        {
            position++;
        }

        while (position < haystack.Length &&
            RegexSimpleSequenceSegment.IsRegexWhitespace(haystack[position]))
        {
            position++;
        }

        nextPosition = position;
        return true;
    }

    private bool TryGetSingleRepeatedAtom(
        out RegexSimpleSequenceSegment segment,
        out int minimum,
        out int? maximum,
        out bool lazy)
    {
        if (repeatedSegments is null)
        {
            if (segments.Length == 1)
            {
                segment = segments[0];
                minimum = segment.Minimum;
                maximum = segment.Maximum;
                lazy = segment.Lazy;
                return true;
            }
        }
        else if (repeatedSegments.Length == 1 &&
            repeatedSegments[0].Minimum == 1 &&
            repeatedSegments[0].Maximum == 1)
        {
            segment = repeatedSegments[0];
            minimum = repeatedMinimum;
            maximum = repeatedMaximum;
            lazy = repeatedLazy;
            return true;
        }

        segment = default;
        minimum = 0;
        maximum = null;
        lazy = false;
        return false;
    }

    private static void CountAsciiLetterRuns(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count,
        ref long spanSum)
    {
        int runLength = 0;
        while (position < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiLetter(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
    }

    private static void CountAsciiDigitRuns(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count,
        ref long spanSum)
    {
        int runLength = 0;
        while (position < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiDigit(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
    }

    private static void CountAsciiWordRuns(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count,
        ref long spanSum)
    {
        int runLength = 0;
        while (position < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiWord(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, ref count, ref spanSum);
    }

    private static RegexMatch? FindSingleSegment(RegexSimpleSequenceSegment segment, ReadOnlySpan<byte> haystack, int startOffset)
    {
        int scan = startOffset;
        while (scan < haystack.Length)
        {
            while (scan < haystack.Length && !segment.AtomMatches(haystack[scan]))
            {
                scan++;
            }

            if (scan >= haystack.Length)
            {
                return null;
            }

            int runStart = scan;
            int maxCount = Math.Min(segment.Maximum ?? haystack.Length - runStart, haystack.Length - runStart);
            if (maxCount <= 0)
            {
                scan++;
                continue;
            }

            int matched = 0;
            while (matched < maxCount && segment.AtomMatches(haystack[runStart + matched]))
            {
                matched++;
            }

            if (matched >= segment.Minimum)
            {
                int length = segment.Lazy
                    ? segment.Minimum
                    : matched;
                return new RegexMatch(runStart, length);
            }

            scan = runStart + matched;
        }

        return null;
    }

    private static void AddRun(int minimum, int? maximum, bool lazy, int runLength, ref long count, ref long spanSum)
    {
        if (runLength < minimum)
        {
            return;
        }

        if (!lazy && !maximum.HasValue)
        {
            count++;
            spanSum += runLength;
            return;
        }

        int matchLength = lazy ? minimum : maximum ?? minimum;
        while (runLength >= minimum)
        {
            int length = Math.Min(matchLength, runLength);
            count++;
            spanSum += length;
            runLength -= length;
        }
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        if (start < 0 || start > haystack.Length)
        {
            length = 0;
            return false;
        }

        if (!CanStartAt(haystack, start))
        {
            length = 0;
            return false;
        }

        if (repeatedSegments is not null)
        {
            return TryMatchRepeatedAt(haystack, start, out length);
        }

        if (segments.Length == 1)
        {
            return TryMatchSingleSegment(segments[0], haystack, start, out length);
        }

        var cache = new Dictionary<long, int>();
        if (TryMatchFrom(segments, 0, start, haystack, cache, out int end))
        {
            length = end - start;
            return true;
        }

        length = 0;
        return false;
    }

    private bool CanStartAt(ReadOnlySpan<byte> haystack, int start)
    {
        RegexSimpleSequenceSegment[] activeSegments = repeatedSegments ?? segments;
        return activeSegments.Length == 0 ||
            activeSegments[0].Minimum == 0 ||
            start < haystack.Length && activeSegments[0].AtomMatches(haystack[start]);
    }

    private bool TryMatchRepeatedAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int position = start;
        int count = 0;
        var ends = new List<int>();
        var cache = new Dictionary<long, int>();
        while (count < repeatedMaximum &&
            TryMatchChildOnce(haystack, position, cache, out int next) &&
            next > position)
        {
            count++;
            position = next;
            ends.Add(position);
        }

        if (count < repeatedMinimum)
        {
            length = 0;
            return false;
        }

        int selectedEnd = repeatedLazy
            ? ends[repeatedMinimum - 1]
            : ends[^1];
        length = selectedEnd - start;
        return true;
    }

    private bool TryMatchChildOnce(ReadOnlySpan<byte> haystack, int start, Dictionary<long, int> cache, out int end)
    {
        cache.Clear();
        return TryMatchFrom(repeatedSegments!, 0, start, haystack, cache, out end);
    }

    private static bool TryMatchSingleSegment(RegexSimpleSequenceSegment segment, ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int maxCount = segment.Maximum ?? haystack.Length - start;
        maxCount = Math.Min(maxCount, haystack.Length - start);
        int matched = 0;
        while (matched < maxCount &&
            start + matched < haystack.Length &&
            segment.AtomMatches(haystack[start + matched]))
        {
            matched++;
        }

        if (matched < segment.Minimum)
        {
            length = 0;
            return false;
        }

        length = segment.Lazy ? segment.Minimum : matched;
        return true;
    }

    private static bool TryMatchFrom(
        RegexSimpleSequenceSegment[] activeSegments,
        int segmentIndex,
        int position,
        ReadOnlySpan<byte> haystack,
        Dictionary<long, int> cache,
        out int end)
    {
        if (segmentIndex == activeSegments.Length)
        {
            end = position;
            return true;
        }

        long key = ((long)segmentIndex << 32) | (uint)position;
        if (cache.TryGetValue(key, out int cached))
        {
            end = cached;
            return cached >= 0;
        }

        RegexSimpleSequenceSegment segment = activeSegments[segmentIndex];
        int maxCount = segment.Maximum ?? haystack.Length - position;
        maxCount = Math.Min(maxCount, haystack.Length - position);

        int matched = 0;
        int scan = position;
        while (matched < maxCount &&
            scan < haystack.Length &&
            segment.AtomMatches(haystack[scan]))
        {
            matched++;
            scan++;
        }

        if (matched >= segment.Minimum)
        {
            if (segment.Lazy)
            {
                for (int count = segment.Minimum; count <= matched; count++)
                {
                    if (TryMatchFrom(activeSegments, segmentIndex + 1, position + count, haystack, cache, out end))
                    {
                        cache[key] = end;
                        return true;
                    }
                }
            }
            else
            {
                for (int count = matched; count >= segment.Minimum; count--)
                {
                    if (TryMatchFrom(activeSegments, segmentIndex + 1, position + count, haystack, cache, out end))
                    {
                        cache[key] = end;
                        return true;
                    }
                }
            }
        }

        end = -1;
        cache[key] = -1;
        return false;
    }

    private static bool TryAppend(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                return true;
            case RegexSyntaxKind.Literal:
                return TryAppendLiteral(((RegexAtomNode)node).Value.Span, options, minimum: 1, maximum: 1, lazy: false, segments);
            case RegexSyntaxKind.Dot:
            case RegexSyntaxKind.AnyClass:
            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
                return TryAppendAtom((RegexAtomNode)node, options, minimum: 1, maximum: 1, lazy: false, segments, ref sawVariableRepetition);
            case RegexSyntaxKind.Sequence:
                return TryAppendSequence((RegexSequenceNode)node, options, segments, ref sawVariableRepetition);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAppend(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), segments, ref sawVariableRepetition);
            case RegexSyntaxKind.Repetition:
                return TryAppendRepetition((RegexRepetitionNode)node, options, segments, ref sawVariableRepetition);
            default:
                return false;
        }
    }

    private static bool TryAppendSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                if (currentOptions.Utf8 || currentOptions.UnicodeClasses)
                {
                    return false;
                }

                continue;
            }

            if (!TryAppend(child, currentOptions, segments, ref sawVariableRepetition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendRepetition(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        if (node.Maximum.HasValue && node.Maximum.Value > MaxBoundedRepeat)
        {
            return false;
        }

        sawVariableRepetition |= node.Maximum != node.Minimum;
        RegexSyntaxNode child = UnwrapTransparentGroups(node.Child);
        if (child.Kind == RegexSyntaxKind.Literal)
        {
            ReadOnlySpan<byte> literal = ((RegexAtomNode)child).Value.Span;
            return literal.Length == 1 &&
                TryAppendLiteral(literal, options, node.Minimum, node.Maximum, node.Lazy, segments);
        }

        if (child is not RegexAtomNode atom || !CanRepeatAtom(atom, node.Maximum, options))
        {
            return false;
        }

        return TryAppendAtom(atom, options, node.Minimum, node.Maximum, node.Lazy, segments, ref sawVariableRepetition);
    }

    private static bool TryAppendLiteral(
        ReadOnlySpan<byte> literal,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        bool lazy,
        List<RegexSimpleSequenceSegment> segments)
    {
        if (literal.Length == 0)
        {
            return false;
        }

        if (literal.Length == 1)
        {
            segments.Add(new RegexSimpleSequenceSegment(
                RegexSyntaxKind.Literal,
                [literal[0]],
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                minimum,
                maximum,
                lazy));
            return true;
        }

        if (minimum != 1 || maximum != 1)
        {
            return false;
        }

        for (int index = 0; index < literal.Length; index++)
        {
            segments.Add(new RegexSimpleSequenceSegment(
                RegexSyntaxKind.Literal,
                [literal[index]],
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                minimum: 1,
                maximum: 1,
                lazy: false));
        }

        return true;
    }

    private static bool TryAppendAtom(
        RegexAtomNode atom,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        bool lazy,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        if (RegexByteClass.RequiresUtf8ScalarMatch(atom.Kind, atom.Value.Span, options.Utf8, options.CaseInsensitive, options.UnicodeClasses))
        {
            return false;
        }

        sawVariableRepetition |= maximum != minimum;
        segments.Add(new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum,
            maximum,
            lazy));
        return true;
    }

    private static bool TryCreateRepeatedRoot(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexSimpleSequenceEngine? engine)
    {
        engine = null;
        root = UnwrapTransparentGroups(root);
        if (root is not RegexRepetitionNode repetition ||
            !repetition.Maximum.HasValue ||
            repetition.Minimum <= 0 ||
            repetition.Maximum.Value > MaxBoundedRepeat)
        {
            return false;
        }

        var childSegments = new List<RegexSimpleSequenceSegment>();
        bool sawVariableRepetition = false;
        if (!TryAppend(repetition.Child, options, childSegments, ref sawVariableRepetition) ||
            childSegments.Count == 0 ||
            childSegments.Count * repetition.Maximum.Value > MaxSegments ||
            !HasSelectiveRequiredStart(childSegments))
        {
            return false;
        }

        engine = new RegexSimpleSequenceEngine(childSegments, repetition.Minimum, repetition.Maximum.Value, repetition.Lazy);
        return true;
    }

    private static bool CanRepeatAtom(RegexAtomNode atom, int? maximum, RegexCompileOptions options)
    {
        if (maximum.HasValue)
        {
            return true;
        }

        return atom.Kind is RegexSyntaxKind.Literal
            or RegexSyntaxKind.DigitClass
            or RegexSyntaxKind.WordClass
            or RegexSyntaxKind.WhitespaceClass ||
            atom.Kind == RegexSyntaxKind.CharacterClass &&
            CountMatchingBytes(atom, options) <= MaxSelectiveStartBytes;
    }

    private static bool HasSelectiveRequiredStart(List<RegexSimpleSequenceSegment> segments)
    {
        return segments.Count > 0 &&
            segments[0].Minimum > 0 &&
            CountMatchingBytes(segments[0]) <= MaxSelectiveStartBytes;
    }

    private static int LongestLiteralRun(List<RegexSimpleSequenceSegment> segments)
    {
        int best = 0;
        int current = 0;
        for (int index = 0; index < segments.Count; index++)
        {
            RegexSimpleSequenceSegment segment = segments[index];
            if (segment.Kind == RegexSyntaxKind.Literal &&
                segment.Minimum == 1 &&
                segment.Maximum == 1)
            {
                current++;
                best = Math.Max(best, current);
            }
            else
            {
                current = 0;
            }
        }

        return best;
    }

    private static int CountMatchingBytes(RegexAtomNode atom, RegexCompileOptions options)
    {
        return CountMatchingBytes(new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum: 1,
            maximum: 1,
            lazy: false));
    }

    private static int CountMatchingBytes(RegexSimpleSequenceSegment segment)
    {
        int count = 0;
        for (int value = 0; value <= 0xFF; value++)
        {
            if (segment.AtomMatches((byte)value))
            {
                count++;
            }
        }

        return count;
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }

}
