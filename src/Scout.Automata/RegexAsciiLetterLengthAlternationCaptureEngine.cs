namespace Scout;

internal sealed class RegexAsciiLetterLengthAlternationCaptureEngine
{
    private readonly int[] lengths;
    private readonly int[] captureIndexes;
    private readonly int captureCount;
    private readonly int minimumLength;

    private RegexAsciiLetterLengthAlternationCaptureEngine(int[] lengths, int[] captureIndexes, int captureCount)
    {
        this.lengths = lengths;
        this.captureIndexes = captureIndexes;
        this.captureCount = captureCount;
        minimumLength = int.MaxValue;
        for (int index = 0; index < lengths.Length; index++)
        {
            minimumLength = Math.Min(minimumLength, lengths[index]);
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexAsciiLetterLengthAlternationCaptureEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses ||
            captureCount <= 0 ||
            UnwrapTransparentNonCapturingGroups(root) is not RegexAlternationNode alternation ||
            alternation.Alternatives.Count == 0)
        {
            return false;
        }

        int[] lengths = new int[alternation.Alternatives.Count];
        int[] captureIndexes = new int[alternation.Alternatives.Count];
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryGetCapturedAsciiLetterRun(
                    alternation.Alternatives[index],
                    out int length,
                    out int captureIndex))
            {
                return false;
            }

            lengths[index] = length;
            captureIndexes[index] = captureIndex;
        }

        engine = new RegexAsciiLetterLengthAlternationCaptureEngine(lengths, captureIndexes, captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        while (start < haystack.Length)
        {
            while (start < haystack.Length && !RegexSimpleSequenceSegment.IsAsciiLetter(haystack[start]))
            {
                start++;
            }

            if (start >= haystack.Length)
            {
                return null;
            }

            int end = start + 1;
            while (end < haystack.Length && RegexSimpleSequenceSegment.IsAsciiLetter(haystack[end]))
            {
                end++;
            }

            int runLength = end - start;
            if (runLength >= minimumLength)
            {
                for (int index = 0; index < lengths.Length; index++)
                {
                    int length = lengths[index];
                    if (runLength >= length)
                    {
                        RegexMatch match = new(start, length);
                        var groups = new RegexMatch?[captureCount + 1];
                        groups[0] = match;
                        groups[captureIndexes[index]] = match;
                        return new RegexCaptures(match, groups);
                    }
                }
            }

            start = end + 1;
        }

        return null;
    }

    private static bool TryGetCapturedAsciiLetterRun(
        RegexSyntaxNode node,
        out int length,
        out int captureIndex)
    {
        length = 0;
        captureIndex = 0;
        if (UnwrapTransparentNonCapturingGroups(node) is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group ||
            UnwrapTransparentNonCapturingGroups(group.Child) is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: { } maximum,
                Lazy: false,
            } repetition ||
            repetition.Minimum != maximum ||
            !IsAsciiLetterAtom(UnwrapTransparentNonCapturingGroups(repetition.Child)))
        {
            return false;
        }

        length = maximum;
        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool IsAsciiLetterAtom(RegexSyntaxNode node)
    {
        return node is RegexAtomNode { Kind: RegexSyntaxKind.LetterClass } ||
            node is RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom &&
            IsAsciiLetterClass(atom.Value.Span);
    }

    private static bool IsAsciiLetterClass(ReadOnlySpan<byte> value)
    {
        return value.SequenceEqual("A-Za-z"u8) || value.SequenceEqual("a-zA-Z"u8);
    }

    private static RegexSyntaxNode UnwrapTransparentNonCapturingGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.NonCapturingGroup,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group)
        {
            node = group.Child;
        }

        return node;
    }
}
