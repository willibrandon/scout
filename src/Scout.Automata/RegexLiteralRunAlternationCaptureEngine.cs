using System.Buffers;

namespace Scout;

internal sealed class RegexLiteralRunAlternationCaptureEngine
{
    private readonly int[] captureIndexesByByte;
    private readonly SearchValues<byte> searchValues;
    private readonly int captureCount;

    private RegexLiteralRunAlternationCaptureEngine(int[] captureIndexesByByte, byte[] literals, int captureCount)
    {
        this.captureIndexesByByte = captureIndexesByByte;
        this.captureCount = captureCount;
        searchValues = SearchValues.Create(literals);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexLiteralRunAlternationCaptureEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            captureCount <= 0 ||
            UnwrapTransparentNonCapturingGroups(root) is not RegexAlternationNode alternation ||
            alternation.Alternatives.Count == 0)
        {
            return false;
        }

        int[] captureIndexesByByte = new int[256];
        List<byte> literals = new();
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryGetCapturedLiteralRun(alternation.Alternatives[index], out byte literal, out int captureIndex))
            {
                return false;
            }

            if (captureIndexesByByte[literal] == 0)
            {
                captureIndexesByByte[literal] = captureIndex;
                literals.Add(literal);
            }
        }

        if (literals.Count == 0)
        {
            return false;
        }

        engine = new RegexLiteralRunAlternationCaptureEngine(captureIndexesByByte, literals.ToArray(), captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        while (start < haystack.Length)
        {
            int offset = haystack[start..].IndexOfAny(searchValues);
            if (offset < 0)
            {
                return null;
            }

            start += offset;
            byte literal = haystack[start];
            int end = start + 1;
            while (end < haystack.Length && haystack[end] == literal)
            {
                end++;
            }

            RegexMatch match = new(start, end - start);
            var groups = new RegexMatch?[captureCount + 1];
            groups[0] = match;
            groups[captureIndexesByByte[literal]] = match;
            return new RegexCaptures(match, groups);
        }

        return null;
    }

    private static bool TryGetCapturedLiteralRun(RegexSyntaxNode node, out byte literal, out int captureIndex)
    {
        literal = 0;
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
                Maximum: null,
                Lazy: false,
            } repetition ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode
            {
                Kind: RegexSyntaxKind.Literal,
            } atom ||
            atom.Value.Length != 1)
        {
            return false;
        }

        literal = atom.Value.Span[0];
        captureIndex = group.CaptureIndex;
        return true;
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
