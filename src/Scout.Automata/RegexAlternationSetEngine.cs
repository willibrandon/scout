namespace Scout;

internal sealed class RegexAlternationSetEngine
{
    private const int MinimumAlternativeCount = 16;

    private readonly PatternSet patternSet;
    private readonly int[]? wholeBranchCaptureByPattern;
    private readonly int captureCount;

    private RegexAlternationSetEngine(PatternSet patternSet, int[]? wholeBranchCaptureByPattern, int captureCount)
    {
        this.patternSet = patternSet;
        this.wholeBranchCaptureByPattern = wholeBranchCaptureByPattern;
        this.captureCount = captureCount;
    }

    public bool CanSynthesizeCaptures => wholeBranchCaptureByPattern is not null;

    public static bool TryCreate(
        ReadOnlySpan<byte> pattern,
        RegexSyntaxNode root,
        int captureCount,
        RegexCompileOptions options,
        out RegexAlternationSetEngine? engine)
    {
        engine = null;
        if (!TrySplitTopLevelAlternation(pattern, flattenNestedAlternatives: true, out byte[][] alternatives) ||
            alternatives.Length < MinimumAlternativeCount)
        {
            return false;
        }

        if (!PatternSet.CanPreflightAccelerateEveryPattern(alternatives, options))
        {
            return false;
        }

        var patternSet = PatternSet.Compile(
            alternatives,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses);
        if (!patternSet.CanAccelerateEveryPattern)
        {
            return false;
        }

        int[]? wholeBranchCaptureByPattern = TryCreateWholeBranchCaptureMap(root, alternatives.Length, out int[]? map)
            ? map
            : null;
        engine = new RegexAlternationSetEngine(
            patternSet,
            wholeBranchCaptureByPattern,
            captureCount);
        return true;
    }

    public static bool TryCreateSyntheticCaptures(
        ReadOnlySpan<byte> pattern,
        RegexSyntaxNode root,
        int captureCount,
        RegexCompileOptions options,
        out RegexAlternationSetEngine? engine)
    {
        engine = null;
        if (!TrySplitTopLevelAlternation(pattern, flattenNestedAlternatives: false, out byte[][] alternatives) ||
            alternatives.Length < MinimumAlternativeCount ||
            !TryCreateWholeBranchCaptureMap(root, alternatives.Length, out int[]? wholeBranchCaptureByPattern))
        {
            return false;
        }

        engine = new RegexAlternationSetEngine(
            PatternSet.Compile(
                alternatives,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                options.UnicodeClasses),
            wholeBranchCaptureByPattern,
            captureCount);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return patternSet.Find(haystack, startAt)?.Match;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        PatternSetMatch? match = patternSet.Find(haystack, startAt);
        return match.HasValue && match.Value.Match.Start == Math.Clamp(startAt, 0, haystack.Length)
            ? match.Value.Match
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return patternSet.CountMatches(haystack, startAt);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return patternSet.SumMatchSpans(haystack, startAt);
    }

    public RegexCaptures? FindSyntheticCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (wholeBranchCaptureByPattern is null)
        {
            return null;
        }

        PatternSetMatch? match = patternSet.Find(haystack, startAt);
        if (!match.HasValue)
        {
            return null;
        }

        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match.Value.Match;
        groups[wholeBranchCaptureByPattern[match.Value.PatternId]] = match.Value.Match;
        return new RegexCaptures(match.Value.Match, groups);
    }

    private static bool TrySplitTopLevelAlternation(
        ReadOnlySpan<byte> pattern,
        bool flattenNestedAlternatives,
        out byte[][] alternatives)
    {
        alternatives = [];
        if ((TryStripWholeEnclosingGroup(pattern, out ReadOnlySpan<byte> inner) ||
                TryStripWholeExactOneRepetition(pattern, out inner)) &&
            TrySplitTopLevelAlternation(inner, flattenNestedAlternatives, out alternatives))
        {
            return true;
        }

        var parts = new List<byte[]>();
        int start = 0;
        int depth = 0;
        bool sawSeparator = false;
        bool inClass = false;
        bool escaped = false;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (value == (byte)'\\')
            {
                escaped = true;
                continue;
            }

            if (inClass)
            {
                if (value == (byte)']')
                {
                    inClass = false;
                }

                continue;
            }

            switch (value)
            {
                case (byte)'[':
                    inClass = true;
                    break;
                case (byte)'(':
                    if (depth == 0 && IsTopLevelInlineFlagDirective(pattern, index))
                    {
                        return false;
                    }

                    depth++;
                    break;
                case (byte)')':
                    if (depth == 0)
                    {
                        return false;
                    }

                    depth--;
                    break;
                case (byte)'|' when depth == 0:
                    if (!AddAlternative(pattern[start..index], parts, flattenNestedAlternatives))
                    {
                        return false;
                    }

                    sawSeparator = true;
                    start = index + 1;
                    break;
            }
        }

        if (!sawSeparator)
        {
            return false;
        }

        if (depth != 0 || inClass || escaped || !AddAlternative(pattern[start..], parts, flattenNestedAlternatives))
        {
            return false;
        }

        alternatives = parts.Count > 1 ? parts.ToArray() : [];
        return alternatives.Length > 0;
    }

    private static bool TryStripWholeExactOneRepetition(ReadOnlySpan<byte> pattern, out ReadOnlySpan<byte> inner)
    {
        inner = [];
        ReadOnlySpan<byte> suffix = "{1,1}"u8;
        return pattern.EndsWith(suffix) &&
            TryStripWholeEnclosingGroup(pattern[..^suffix.Length], out inner);
    }

    private static bool TryStripWholeEnclosingGroup(ReadOnlySpan<byte> pattern, out ReadOnlySpan<byte> inner)
    {
        inner = [];
        if (pattern.Length < 2 || pattern[0] != (byte)'(')
        {
            return false;
        }

        int innerStart = 1;
        if (pattern.Length >= 3 && pattern[1] == (byte)'?')
        {
            if (pattern[2] != (byte)':')
            {
                return false;
            }

            innerStart = 3;
        }

        int depth = 0;
        bool inClass = false;
        bool escaped = false;
        for (int index = innerStart; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (value == (byte)'\\')
            {
                escaped = true;
                continue;
            }

            if (inClass)
            {
                if (value == (byte)']')
                {
                    inClass = false;
                }

                continue;
            }

            switch (value)
            {
                case (byte)'[':
                    inClass = true;
                    break;
                case (byte)'(':
                    depth++;
                    break;
                case (byte)')':
                    if (depth == 0)
                    {
                        if (index == pattern.Length - 1)
                        {
                            inner = pattern[innerStart..index];
                            return !inner.IsEmpty;
                        }

                        if (index + 6 == pattern.Length &&
                            pattern.Slice(index + 1, 5).SequenceEqual("{1,1}"u8))
                        {
                            inner = pattern[innerStart..index];
                            return !inner.IsEmpty;
                        }

                        if (index != pattern.Length - 1)
                        {
                            return false;
                        }
                    }

                    depth--;
                    break;
            }
        }

        return false;
    }

    private static bool IsTopLevelInlineFlagDirective(ReadOnlySpan<byte> pattern, int openParen)
    {
        int index = openParen + 1;
        if (index >= pattern.Length || pattern[index] != (byte)'?')
        {
            return false;
        }

        index++;
        bool hasFlag = false;
        while (index < pattern.Length)
        {
            byte token = pattern[index];
            if (token == (byte)':')
            {
                return false;
            }

            if (token == (byte)')')
            {
                return hasFlag;
            }

            if (token == (byte)'-')
            {
                index++;
                continue;
            }

            if (!IsRegexFlagByte(token))
            {
                return false;
            }

            hasFlag = true;
            index++;
        }

        return false;
    }

    private static bool IsRegexFlagByte(byte value)
    {
        return value is (byte)'i' or (byte)'m' or (byte)'s' or (byte)'U' or (byte)'x';
    }

    private static bool AddAlternative(
        ReadOnlySpan<byte> alternative,
        List<byte[]> alternatives,
        bool flattenNestedAlternatives)
    {
        if (alternative.IsEmpty)
        {
            return false;
        }

        if (flattenNestedAlternatives &&
            TrySplitTopLevelAlternation(alternative, flattenNestedAlternatives, out byte[][] nestedAlternatives))
        {
            alternatives.AddRange(nestedAlternatives);
            return true;
        }

        alternatives.Add(alternative.ToArray());
        return true;
    }

    private static bool TryCreateWholeBranchCaptureMap(RegexSyntaxNode root, int alternativeCount, out int[]? map)
    {
        map = null;
        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexAlternationNode alternation ||
            alternation.Alternatives.Count != alternativeCount)
        {
            return false;
        }

        int[] captures = new int[alternativeCount];
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            RegexSyntaxNode alternative = UnwrapTransparentNonCapturingGroups(alternation.Alternatives[index]);
            if (alternative is not RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group ||
                group.CaptureIndex <= 0 ||
                HasCapturingGroup(group.Child))
            {
                return false;
            }

            captures[index] = group.CaptureIndex;
        }

        map = captures;
        return true;
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }

    private static bool HasCapturingGroup(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.CapturingGroup:
                return true;
            case RegexSyntaxKind.NonCapturingGroup:
                return HasCapturingGroup(((RegexGroupNode)node).Child);
            case RegexSyntaxKind.Sequence:
                var sequence = (RegexSequenceNode)node;
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (HasCapturingGroup(sequence.Nodes[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.Alternation:
                var alternation = (RegexAlternationNode)node;
                for (int index = 0; index < alternation.Alternatives.Count; index++)
                {
                    if (HasCapturingGroup(alternation.Alternatives[index]))
                    {
                        return true;
                    }
                }

                return false;
            case RegexSyntaxKind.Repetition:
                return HasCapturingGroup(((RegexRepetitionNode)node).Child);
            default:
                return false;
        }
    }
}
