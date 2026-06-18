using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexAnchoredWordCaptureEngine
{
    private static readonly SearchValues<byte> AsciiWordBytes = SearchValues.Create(
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz"u8);

    private readonly int[] captureIndexes;
    private readonly bool unicodeWord;
    private readonly int captureCount;

    private RegexAnchoredWordCaptureEngine(int[] captureIndexes, bool unicodeWord, int captureCount)
    {
        this.captureIndexes = captureIndexes;
        this.unicodeWord = unicodeWord;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexAnchoredWordCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 || options.MultiLine)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 4 ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[0]) is not RegexAtomNode { Kind: RegexSyntaxKind.StartAnchor })
        {
            return false;
        }

        int index = 1;
        if (index < sequence.Nodes.Count && TryConsumeSpaceRun(sequence.Nodes[index], minimum: 0))
        {
            index++;
        }

        List<int> captures = [];
        while (index < sequence.Nodes.Count)
        {
            if (!TryGetWordCapture(sequence.Nodes[index], out int captureIndex))
            {
                return false;
            }

            captures.Add(captureIndex);
            index++;
            if (index >= sequence.Nodes.Count)
            {
                break;
            }

            if (!TryConsumeSpaceRun(sequence.Nodes[index], minimum: 1))
            {
                return false;
            }

            index++;
        }

        if (captures.Count == 0)
        {
            return false;
        }

        engine = new RegexAnchoredWordCaptureEngine(captures.ToArray(), options.UnicodeClasses, captureCount);
        return true;
    }

    public RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (startAt != 0)
        {
            return null;
        }

        int position = ConsumeSpaces(haystack, 0);
        var groups = new RegexMatch?[captureCount + 1];
        for (int index = 0; index < captureIndexes.Length; index++)
        {
            int captureStart = position;
            position = ConsumeWordRun(haystack, position);
            if (position == captureStart)
            {
                return null;
            }

            groups[captureIndexes[index]] = new RegexMatch(captureStart, position - captureStart);
            if (index == captureIndexes.Length - 1)
            {
                break;
            }

            int spaceStart = position;
            position = ConsumeSpaces(haystack, position);
            if (position == spaceStart)
            {
                return null;
            }
        }

        var match = new RegexMatch(0, position);
        groups[0] = match;
        return new RegexCaptures(match, groups);
    }

    private int ConsumeWordRun(ReadOnlySpan<byte> haystack, int position)
    {
        if (!unicodeWord)
        {
            ReadOnlySpan<byte> remaining = haystack[position..];
            int offset = remaining.IndexOfAnyExcept(AsciiWordBytes);
            return offset < 0 ? haystack.Length : position + offset;
        }

        while (TryWordMatchLength(haystack, position, out int length))
        {
            position += length;
        }

        return position;
    }

    private bool TryWordMatchLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            if (RegexSimpleSequenceSegment.IsAsciiWord(first))
            {
                length = 1;
                return true;
            }

            return false;
        }

        if (!unicodeWord)
        {
            return false;
        }

        if ((first == 0xD0 || first == 0xD1) &&
            position + 1 < haystack.Length &&
            haystack[position + 1] is >= 0x80 and <= 0xBF)
        {
            length = 2;
            return true;
        }

        if (Rune.DecodeFromUtf8(haystack[position..], out Rune rune, out int consumed) != OperationStatus.Done ||
            !RegexUnicodeTables.IsPerlWord(rune))
        {
            return false;
        }

        length = consumed;
        return true;
    }

    private static bool TryGetWordCapture(RegexSyntaxNode node, out int captureIndex)
    {
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
                Minimum: 1,
                Maximum: null,
                Lazy: false,
            } repetition ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.WordClass })
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool TryConsumeSpaceRun(RegexSyntaxNode node, int minimum)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexRepetitionNode
            {
                Minimum: var actualMinimum,
                Maximum: null,
                Lazy: false,
            } repetition)
        {
            return actualMinimum == minimum && IsSpaceLiteral(repetition.Child);
        }

        return minimum == 1 && IsSpaceLiteral(node);
    }

    private static bool IsSpaceLiteral(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexAtomNode
        {
            Kind: RegexSyntaxKind.Literal,
            Value.Length: 1,
        } literal && literal.Value.Span[0] == (byte)' ';
    }

    private static int ConsumeSpaces(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length && haystack[position] == (byte)' ')
        {
            position++;
        }

        return position;
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
