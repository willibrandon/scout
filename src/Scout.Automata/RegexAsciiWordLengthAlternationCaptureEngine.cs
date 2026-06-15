namespace Scout;

internal sealed class RegexAsciiWordLengthAlternationCaptureEngine
{
    private readonly int[] lengths;
    private readonly int[] captureIndexes;
    private readonly int captureCount;

    private RegexAsciiWordLengthAlternationCaptureEngine(int[] lengths, int[] captureIndexes, int captureCount)
    {
        this.lengths = lengths;
        this.captureIndexes = captureIndexes;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexAsciiWordLengthAlternationCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 ||
            !TryCollectItems(root, options, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items) ||
            items.Count != 3 ||
            !TryGetWordBoundary(items[0]) ||
            !TryGetWordBoundary(items[2]) ||
            !TryGetAlternation(items[1], out RegexAlternationNode? alternation, out RegexCompileOptions alternationOptions) ||
            !CanUseAsciiWordMode(alternationOptions) ||
            alternation is null ||
            alternation.Alternatives.Count == 0)
        {
            return false;
        }

        int[] lengths = new int[alternation.Alternatives.Count];
        int[] captureIndexes = new int[alternation.Alternatives.Count];
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryGetCapturedFixedWordRun(
                    alternation.Alternatives[index],
                    alternationOptions,
                    out int length,
                    out int captureIndex))
            {
                return false;
            }

            lengths[index] = length;
            captureIndexes[index] = captureIndex;
        }

        engine = new RegexAsciiWordLengthAlternationCaptureEngine(lengths, captureIndexes, captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int position = SkipPartialAsciiWord(haystack, Math.Clamp(startAt, 0, haystack.Length));
        while (position < haystack.Length)
        {
            while (position < haystack.Length && !IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int start = position;
            while (position < haystack.Length && IsAsciiWord(haystack[position]))
            {
                position++;
            }

            int runLength = position - start;
            for (int index = 0; index < lengths.Length; index++)
            {
                if (runLength == lengths[index])
                {
                    RegexMatch match = new(start, runLength);
                    var groups = new RegexMatch?[captureCount + 1];
                    groups[0] = match;
                    groups[captureIndexes[index]] = match;
                    return new RegexCaptures(match, groups);
                }
            }
        }

        return null;
    }

    private static int SkipPartialAsciiWord(ReadOnlySpan<byte> haystack, int position)
    {
        if (position > 0 &&
            position < haystack.Length &&
            IsAsciiWord(haystack[position - 1]) &&
            IsAsciiWord(haystack[position]))
        {
            do
            {
                position++;
            }
            while (position < haystack.Length && IsAsciiWord(haystack[position]));
        }

        return position;
    }

    private static bool TryGetWordBoundary((RegexSyntaxNode Node, RegexCompileOptions Options) item)
    {
        return TryUnwrapWithOptions(item.Node, item.Options, out RegexSyntaxNode node, out RegexCompileOptions options) &&
            CanUseAsciiWordMode(options) &&
            node is RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary };
    }

    private static bool TryGetAlternation(
        (RegexSyntaxNode Node, RegexCompileOptions Options) item,
        out RegexAlternationNode? alternation,
        out RegexCompileOptions options)
    {
        alternation = null;
        options = default;
        if (!TryUnwrapWithOptions(item.Node, item.Options, out RegexSyntaxNode node, out options) ||
            node is not RegexAlternationNode actualAlternation)
        {
            return false;
        }

        alternation = actualAlternation;
        return true;
    }

    private static bool TryGetCapturedFixedWordRun(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out int length,
        out int captureIndex)
    {
        length = 0;
        captureIndex = 0;
        if (!TryUnwrapTransparentNonCapturingWithOptions(node, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions groupOptions) ||
            unwrapped is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
            } group ||
            !TryUnwrapTransparentNonCapturingWithOptions(group.Child, groupOptions, out RegexSyntaxNode child, out RegexCompileOptions repetitionOptions) ||
            child is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: { } maximum,
                Lazy: false,
            } repetition ||
            repetition.Minimum != maximum ||
            !TryUnwrapTransparentNonCapturingWithOptions(repetition.Child, repetitionOptions, out RegexSyntaxNode atom, out RegexCompileOptions atomOptions) ||
            !CanUseAsciiWordMode(atomOptions) ||
            !IsWordAtom(atom, atomOptions))
        {
            return false;
        }

        length = maximum;
        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool TryCollectItems(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
    {
        items = [];
        if (!TryUnwrapWithOptions(root, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions))
        {
            return false;
        }

        if (unwrapped is RegexSequenceNode sequence)
        {
            return TryCollectSequenceItems(sequence, effectiveOptions, out items);
        }

        items.Add((unwrapped, effectiveOptions));
        return true;
    }

    private static bool TryCollectSequenceItems(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items)
    {
        items = [];
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            items.Add((child, currentOptions));
        }

        return true;
    }

    private static bool TryUnwrapWithOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSyntaxNode unwrapped,
        out RegexCompileOptions effectiveOptions)
    {
        while (node is RegexGroupNode group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        unwrapped = node;
        effectiveOptions = options;
        return true;
    }

    private static bool TryUnwrapTransparentNonCapturingWithOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSyntaxNode unwrapped,
        out RegexCompileOptions effectiveOptions)
    {
        while (node is RegexGroupNode { Kind: RegexSyntaxKind.NonCapturingGroup } group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        unwrapped = node;
        effectiveOptions = options;
        return true;
    }

    private static bool CanUseAsciiWordMode(RegexCompileOptions options)
    {
        return !options.CaseInsensitive &&
            !options.Utf8 &&
            !options.UnicodeClasses;
    }

    private static bool IsWordAtom(RegexSyntaxNode node, RegexCompileOptions options)
    {
        if (node is not RegexAtomNode atom)
        {
            return false;
        }

        if (atom.Kind == RegexSyntaxKind.WordClass)
        {
            return true;
        }

        if (atom.Kind != RegexSyntaxKind.CharacterClass)
        {
            return false;
        }

        ReadOnlySpan<byte> expression = atom.Value.Span;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bool matches = RegexByteClass.AtomMatches(
                (byte)value,
                RegexSyntaxKind.CharacterClass,
                expression,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
            if (matches != IsAsciiWord((byte)value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiWord(byte value)
    {
        return RegexSimpleSequenceSegment.IsAsciiWord(value);
    }
}
