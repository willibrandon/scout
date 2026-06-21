using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexScalarRunCaptureEngine
{
    private const int MaxBoundedRepeat = 1024;

    private readonly RegexScalarRunAtom atom;
    private readonly RegexCompileOptions options;
    private readonly int[] lengths;
    private readonly int[] captureIndexes;
    private readonly int captureCount;
    private readonly int maximumLength;
    private readonly bool requireWordBoundaries;

    private RegexScalarRunCaptureEngine(
        RegexScalarRunAtom atom,
        RegexCompileOptions options,
        int[] lengths,
        int[] captureIndexes,
        int captureCount,
        bool requireWordBoundaries)
    {
        this.atom = atom;
        this.options = options;
        this.lengths = lengths;
        this.captureIndexes = captureIndexes;
        this.captureCount = captureCount;
        this.requireWordBoundaries = requireWordBoundaries;
        maximumLength = 0;
        for (int index = 0; index < lengths.Length; index++)
        {
            maximumLength = Math.Max(maximumLength, lengths[index]);
        }
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexScalarRunCaptureEngine? engine)
    {
        engine = null;
        if (!options.UnicodeClasses ||
            captureCount <= 0 ||
            !TryGetAlternation(root, out RegexAlternationNode alternation, out bool requireWordBoundaries) ||
            alternation.Alternatives.Count == 0)
        {
            return false;
        }

        int[] lengths = new int[alternation.Alternatives.Count];
        int[] captureIndexes = new int[alternation.Alternatives.Count];
        RegexScalarRunAtom? firstAtom = null;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryGetCapturedFixedScalarRun(
                    alternation.Alternatives[index],
                    options,
                    out RegexScalarRunAtom branchAtom,
                    out int length,
                    out int captureIndex))
            {
                return false;
            }

            if (firstAtom.HasValue && !ScalarAtomsEqual(firstAtom.Value, branchAtom))
            {
                return false;
            }

            firstAtom ??= branchAtom;
            lengths[index] = length;
            captureIndexes[index] = captureIndex;
        }

        if (!firstAtom.HasValue)
        {
            return false;
        }

        engine = new RegexScalarRunCaptureEngine(firstAtom.Value, options, lengths, captureIndexes, captureCount, requireWordBoundaries);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        Span<int> ends = maximumLength <= 64 ? stackalloc int[maximumLength + 1] : new int[maximumLength + 1];
        while (start < haystack.Length)
        {
            int scalarCount = CountScalarRun(haystack, start, ends);
            if (scalarCount > 0)
            {
                if (requireWordBoundaries && !IsWordBoundary(haystack, start))
                {
                    start = ends[scalarCount];
                    continue;
                }

                for (int index = 0; index < lengths.Length; index++)
                {
                    int length = lengths[index];
                    if (scalarCount >= length &&
                        (!requireWordBoundaries || IsWordBoundary(haystack, ends[length])))
                    {
                        RegexMatch match = new(start, ends[length] - start);
                        var groups = new RegexMatch?[captureCount + 1];
                        groups[0] = match;
                        groups[captureIndexes[index]] = match;
                        return new RegexCaptures(match, groups);
                    }
                }

                start = requireWordBoundaries ? ends[scalarCount] : ends[1];
                continue;
            }

            start = AdvanceAfterNonMatch(haystack, start);
        }

        return null;
    }

    private int CountScalarRun(ReadOnlySpan<byte> haystack, int start, Span<int> ends)
    {
        int position = start;
        int count = 0;
        ends[0] = start;
        while (count < maximumLength &&
            TryAtomMatchLength(haystack, position, out int scalarLength))
        {
            position += scalarLength;
            count++;
            ends[count] = position;
        }

        return count;
    }

    private bool TryAtomMatchLength(ReadOnlySpan<byte> haystack, int position, out int scalarLength)
    {
        if (atom.UnicodeLetterFastPath &&
            TryFastUnicodeLetterMatchLength(haystack, position, out bool matched, out scalarLength))
        {
            return matched;
        }

        if (atom.CyrillicWordFastPath &&
            TryFastCyrillicWordMatchLength(haystack, position, out matched, out scalarLength))
        {
            return matched;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            atom.Kind,
            atom.Value,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out scalarLength);
    }

    private bool IsWordBoundary(ReadOnlySpan<byte> haystack, int position)
    {
        return RegexByteClass.PredicateMatches(
            haystack,
            position,
            RegexSyntaxKind.WordBoundary,
            options.MultiLine,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses);
    }

    private static bool TryGetAlternation(
        RegexSyntaxNode root,
        out RegexAlternationNode alternation,
        out bool requireWordBoundaries)
    {
        alternation = null!;
        requireWordBoundaries = false;
        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is RegexAlternationNode plainAlternation)
        {
            alternation = plainAlternation;
            return true;
        }

        if (root is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsWordBoundary(sequence.Nodes[0]) &&
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[1]) is RegexAlternationNode boundedAlternation &&
            IsWordBoundary(sequence.Nodes[2]))
        {
            alternation = boundedAlternation;
            requireWordBoundaries = true;
            return true;
        }

        return false;
    }

    private static bool IsWordBoundary(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary };
    }

    private static bool TryGetCapturedFixedScalarRun(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexScalarRunAtom atom,
        out int length,
        out int captureIndex)
    {
        atom = default;
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
            maximum > MaxBoundedRepeat ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode atomNode ||
            !RegexScalarRunEngine.TryCreateScalarAtom(atomNode, options, out atom, out _))
        {
            return false;
        }

        length = maximum;
        captureIndex = group.CaptureIndex;
        return true;
    }

    private static bool ScalarAtomsEqual(RegexScalarRunAtom left, RegexScalarRunAtom right)
    {
        return left.Kind == right.Kind &&
            left.UnicodeLetterFastPath == right.UnicodeLetterFastPath &&
            left.CyrillicWordFastPath == right.CyrillicWordFastPath &&
            left.Value.AsSpan().SequenceEqual(right.Value);
    }

    private static int AdvanceAfterNonMatch(ReadOnlySpan<byte> haystack, int position)
    {
        if (haystack[position] <= 0x7F)
        {
            return position + 1;
        }

        return RegexByteClass.IsUtf8Boundary(haystack, position) &&
            Rune.DecodeFromUtf8(haystack[position..], out _, out int length) == OperationStatus.Done
            ? position + length
            : position + 1;
    }

    private static bool TryFastUnicodeLetterMatchLength(
        ReadOnlySpan<byte> haystack,
        int position,
        out bool matched,
        out int scalarLength)
    {
        matched = false;
        scalarLength = 0;
        if (position >= haystack.Length)
        {
            return true;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            matched = RegexSimpleSequenceSegment.IsAsciiLetter(first);
            scalarLength = matched ? 1 : 0;
            return true;
        }

        if ((first == 0xD0 || first == 0xD1) &&
            position + 1 < haystack.Length &&
            haystack[position + 1] is >= 0x80 and <= 0xBF)
        {
            matched = true;
            scalarLength = 2;
            return true;
        }

        return false;
    }

    private static bool TryFastCyrillicWordMatchLength(
        ReadOnlySpan<byte> haystack,
        int position,
        out bool matched,
        out int scalarLength)
    {
        matched = false;
        scalarLength = 0;
        if (position >= haystack.Length)
        {
            return true;
        }

        byte first = haystack[position];
        if (first <= 0xCF)
        {
            return true;
        }

        if (first == 0xD0 || first == 0xD1)
        {
            if (position + 1 < haystack.Length &&
                haystack[position + 1] is >= 0x80 and <= 0xBF)
            {
                matched = true;
                scalarLength = 2;
            }

            return true;
        }

        return false;
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
