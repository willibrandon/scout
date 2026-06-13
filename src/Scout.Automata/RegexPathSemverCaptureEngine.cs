namespace Scout;

internal sealed class RegexPathSemverCaptureEngine
{
    private const int MaxBranchCount = 4;

    private readonly RegexPathSemverCaptureBranch[] branches;
    private readonly byte[] commonPrefix;
    private readonly MemmemFinder commonPrefixFinder;
    private readonly int captureCount;

    private RegexPathSemverCaptureEngine(
        RegexPathSemverCaptureBranch[] branches,
        byte[] commonPrefix,
        int captureCount)
    {
        this.branches = branches;
        this.commonPrefix = commonPrefix;
        this.captureCount = captureCount;
        commonPrefixFinder = new MemmemFinder(commonPrefix);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexPathSemverCaptureEngine? engine)
    {
        engine = null;
        if (captureCount <= 0 ||
            options.CaseInsensitive ||
            options.Utf8 ||
            options.SwapGreed)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        List<RegexPathSemverCaptureBranch> branches = [];
        if (root is RegexAlternationNode alternation)
        {
            if (alternation.Alternatives.Count == 0 ||
                alternation.Alternatives.Count > MaxBranchCount)
            {
                return false;
            }

            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryGetBranch(alternation.Alternatives[index], options, out RegexPathSemverCaptureBranch? branch))
                {
                    return false;
                }

                branches.Add(branch!);
            }
        }
        else
        {
            if (!TryGetBranch(root, options, out RegexPathSemverCaptureBranch? branch))
            {
                return false;
            }

            branches.Add(branch!);
        }

        byte[]? commonPrefix = TryGetCommonPrefix(branches);
        if (commonPrefix is null)
        {
            return false;
        }

        engine = new RegexPathSemverCaptureEngine(branches.ToArray(), commonPrefix, captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt <= haystack.Length - commonPrefix.Length)
        {
            int offset = commonPrefixFinder.Find(haystack[searchAt..]);
            if (offset < 0)
            {
                return null;
            }

            int start = searchAt + offset;
            for (int index = 0; index < branches.Length; index++)
            {
                if (TryMatchBranch(haystack, start, branches[index], out RegexCaptures? captures))
                {
                    return captures;
                }
            }

            searchAt = start + 1;
        }

        return null;
    }

    private bool TryMatchBranch(
        ReadOnlySpan<byte> haystack,
        int start,
        RegexPathSemverCaptureBranch branch,
        out RegexCaptures? captures)
    {
        captures = null;
        int position = start;
        for (int index = 0; index < branch.PrefixParts.Length; index++)
        {
            RegexPathSemverPrefixPart part = branch.PrefixParts[index];
            if (part.Literal is not null)
            {
                ReadOnlySpan<byte> literal = part.Literal;
                if (literal.Length > haystack.Length - position ||
                    !haystack.Slice(position, literal.Length).SequenceEqual(literal))
                {
                    return false;
                }

                position += literal.Length;
                continue;
            }

            if ((uint)position >= (uint)haystack.Length ||
                part.ByteMatches is null ||
                !part.ByteMatches[haystack[position]])
            {
                return false;
            }

            position++;
        }

        int directoryStart = position;
        position = ConsumeMatchingBytes(haystack, position, branch.DirectoryByteMatches);
        if (position == directoryStart ||
            !TryConsumeByteSet(haystack, ref position, branch.DirectorySeparatorMatches))
        {
            return false;
        }

        int nameStart = position;
        int nameRunEnd = ConsumeMatchingBytes(haystack, position, branch.NameByteMatches);
        if (!TryFindNameVersionSplit(
            haystack,
            nameStart,
            nameRunEnd,
            branch,
            out int nameEnd,
            out int versionStart,
            out int versionEnd,
            out position))
        {
            return false;
        }

        var match = new RegexMatch(start, position - start);
        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;
        groups[branch.NameCaptureIndex] = new RegexMatch(nameStart, nameEnd - nameStart);
        groups[branch.VersionCaptureIndex] = new RegexMatch(versionStart, versionEnd - versionStart);
        captures = new RegexCaptures(match, groups);
        return true;
    }

    private static bool TryFindNameVersionSplit(
        ReadOnlySpan<byte> haystack,
        int nameStart,
        int nameRunEnd,
        RegexPathSemverCaptureBranch branch,
        out int nameEnd,
        out int versionStart,
        out int versionEnd,
        out int matchEnd)
    {
        nameEnd = 0;
        versionStart = 0;
        versionEnd = 0;
        matchEnd = 0;
        for (int hyphen = nameRunEnd - 1; hyphen > nameStart; hyphen--)
        {
            if (haystack[hyphen] != (byte)'-')
            {
                continue;
            }

            int position = hyphen + 1;
            if (!TryConsumeVersion(haystack, ref position, branch))
            {
                continue;
            }

            int candidateVersionEnd = position;
            if (!TryConsumeByteSet(haystack, ref position, branch.TrailingSeparatorMatches))
            {
                continue;
            }

            nameEnd = hyphen;
            versionStart = hyphen + 1;
            versionEnd = candidateVersionEnd;
            matchEnd = position;
            return true;
        }

        return false;
    }

    private static bool TryConsumeVersion(
        ReadOnlySpan<byte> haystack,
        ref int position,
        RegexPathSemverCaptureBranch branch)
    {
        if (!TryConsumeRequiredRun(haystack, ref position, branch.MajorByteMatches) ||
            !TryConsumeByte(haystack, ref position, (byte)'.') ||
            !TryConsumeRequiredRun(haystack, ref position, branch.MinorByteMatches) ||
            !TryConsumeByte(haystack, ref position, (byte)'.') ||
            !TryConsumeRequiredRun(haystack, ref position, branch.PatchByteMatches))
        {
            return false;
        }

        position = ConsumeMatchingBytes(haystack, position, branch.VersionTailByteMatches);
        return true;
    }

    private static bool TryConsumeRequiredRun(ReadOnlySpan<byte> haystack, ref int position, bool[] matches)
    {
        int start = position;
        position = ConsumeMatchingBytes(haystack, position, matches);
        return position > start;
    }

    private static int ConsumeMatchingBytes(ReadOnlySpan<byte> haystack, int position, bool[] matches)
    {
        while (position < haystack.Length && matches[haystack[position]])
        {
            position++;
        }

        return position;
    }

    private static bool TryConsumeByteSet(ReadOnlySpan<byte> haystack, ref int position, bool[] matches)
    {
        if ((uint)position >= (uint)haystack.Length ||
            !matches[haystack[position]])
        {
            return false;
        }

        position++;
        return true;
    }

    private static bool TryConsumeByte(ReadOnlySpan<byte> haystack, ref int position, byte expected)
    {
        if ((uint)position >= (uint)haystack.Length ||
            haystack[position] != expected)
        {
            return false;
        }

        position++;
        return true;
    }

    private static bool TryGetBranch(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexPathSemverCaptureBranch? branch)
    {
        branch = null;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexSequenceNode sequence)
        {
            return false;
        }

        int index = 0;
        var prefixParts = new List<RegexPathSemverPrefixPart>();
        var searchPrefix = new List<byte>();
        bool canExtendSearchPrefix = true;
        while (index < sequence.Nodes.Count)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (TryGetRunByteSet(child, options, minimum: 1, maximum: null, out bool[]? directoryByteMatches))
            {
                index++;
                if (prefixParts.Count == 0 ||
                    searchPrefix.Count < 2 ||
                    !TryReadSeparatorAtom(sequence.Nodes, ref index, options, out bool[]? directorySeparatorMatches) ||
                    !TryGetRunCapture(sequence.Nodes, ref index, options, minimum: 1, maximum: null, out int nameCaptureIndex, out bool[]? nameByteMatches) ||
                    !TryReadLiteralByte(sequence.Nodes, ref index, (byte)'-') ||
                    !TryGetSemverCapture(
                        sequence.Nodes,
                        ref index,
                        options,
                        out int versionCaptureIndex,
                        out bool[]? majorByteMatches,
                        out bool[]? minorByteMatches,
                        out bool[]? patchByteMatches,
                        out bool[]? versionTailByteMatches) ||
                    !TryReadSeparatorAtom(sequence.Nodes, ref index, options, out bool[]? trailingSeparatorMatches) ||
                    index != sequence.Nodes.Count)
                {
                    return false;
                }

                branch = new RegexPathSemverCaptureBranch(
                    prefixParts.ToArray(),
                    searchPrefix.ToArray(),
                    directoryByteMatches!,
                    directorySeparatorMatches!,
                    nameCaptureIndex,
                    nameByteMatches!,
                    versionCaptureIndex,
                    majorByteMatches!,
                    minorByteMatches!,
                    patchByteMatches!,
                    versionTailByteMatches!,
                    trailingSeparatorMatches!);
                return true;
            }

            if (TryGetLiteral(child, out byte[] literal))
            {
                AddLiteralPrefixPart(prefixParts, literal);
                if (canExtendSearchPrefix)
                {
                    searchPrefix.AddRange(literal);
                }

                index++;
                continue;
            }

            if (TryGetSeparatorAtom(child, options, out bool[]? separatorMatches))
            {
                prefixParts.Add(new RegexPathSemverPrefixPart(Literal: null, separatorMatches));
                canExtendSearchPrefix = false;
                index++;
                continue;
            }

            return false;
        }

        return false;
    }

    private static void AddLiteralPrefixPart(List<RegexPathSemverPrefixPart> prefixParts, byte[] literal)
    {
        if (prefixParts.Count == 0 ||
            prefixParts[^1].Literal is null)
        {
            prefixParts.Add(new RegexPathSemverPrefixPart(literal, ByteMatches: null));
            return;
        }

        byte[] previous = prefixParts[^1].Literal!;
        byte[] combined = new byte[previous.Length + literal.Length];
        previous.CopyTo(combined, 0);
        literal.CopyTo(combined, previous.Length);
        prefixParts[^1] = new RegexPathSemverPrefixPart(combined, ByteMatches: null);
    }

    private static bool TryGetRunCapture(
        IReadOnlyList<RegexSyntaxNode> nodes,
        ref int index,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        out int captureIndex,
        out bool[]? matches)
    {
        captureIndex = 0;
        matches = null;
        if (index >= nodes.Count ||
            UnwrapTransparentNonCapturingGroups(nodes[index]) is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group ||
            !TryGetRunByteSet(group.Child, options, minimum, maximum, out matches))
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        index++;
        return true;
    }

    private static bool TryGetSemverCapture(
        IReadOnlyList<RegexSyntaxNode> nodes,
        ref int index,
        RegexCompileOptions options,
        out int captureIndex,
        out bool[]? majorByteMatches,
        out bool[]? minorByteMatches,
        out bool[]? patchByteMatches,
        out bool[]? versionTailByteMatches)
    {
        captureIndex = 0;
        majorByteMatches = null;
        minorByteMatches = null;
        patchByteMatches = null;
        versionTailByteMatches = null;
        if (index >= nodes.Count ||
            UnwrapTransparentNonCapturingGroups(nodes[index]) is not RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                CaptureIndex: > 0,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } group ||
            UnwrapTransparentNonCapturingGroups(group.Child) is not RegexSequenceNode { Nodes.Count: 6 } sequence)
        {
            return false;
        }

        int versionIndex = 0;
        if (!TryReadRunByteSet(sequence.Nodes, ref versionIndex, options, minimum: 1, maximum: null, out majorByteMatches) ||
            !TryReadLiteralByte(sequence.Nodes, ref versionIndex, (byte)'.') ||
            !TryReadRunByteSet(sequence.Nodes, ref versionIndex, options, minimum: 1, maximum: null, out minorByteMatches) ||
            !TryReadLiteralByte(sequence.Nodes, ref versionIndex, (byte)'.') ||
            !TryReadRunByteSet(sequence.Nodes, ref versionIndex, options, minimum: 1, maximum: null, out patchByteMatches) ||
            !TryReadRunByteSet(sequence.Nodes, ref versionIndex, options, minimum: 0, maximum: null, out versionTailByteMatches) ||
            versionIndex != sequence.Nodes.Count)
        {
            return false;
        }

        captureIndex = group.CaptureIndex;
        index++;
        return true;
    }

    private static bool TryReadRunByteSet(
        IReadOnlyList<RegexSyntaxNode> nodes,
        ref int index,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        out bool[]? matches)
    {
        matches = null;
        if (index >= nodes.Count ||
            !TryGetRunByteSet(nodes[index], options, minimum, maximum, out matches))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryGetRunByteSet(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        out bool[]? matches)
    {
        matches = null;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexRepetitionNode
            {
                Lazy: false,
            } repetition ||
            repetition.Minimum != minimum ||
            repetition.Maximum != maximum ||
            UnwrapTransparentNonCapturingGroups(repetition.Child) is not RegexAtomNode atom ||
            !TryCreateByteSet(atom, options, out matches))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadSeparatorAtom(
        IReadOnlyList<RegexSyntaxNode> nodes,
        ref int index,
        RegexCompileOptions options,
        out bool[]? matches)
    {
        matches = null;
        if (index >= nodes.Count ||
            !TryGetSeparatorAtom(nodes[index], options, out matches))
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryGetSeparatorAtom(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out bool[]? matches)
    {
        matches = null;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAtomNode atom ||
            !TryCreateByteSet(atom, options, out matches))
        {
            return false;
        }

        bool[] separatorMatches = matches!;
        int count = 0;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (separatorMatches[value])
            {
                count++;
            }
        }

        return count is >= 1 and <= 2 &&
            (separatorMatches[(byte)'/'] || separatorMatches[(byte)'\\']);
    }

    private static bool TryCreateByteSet(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out bool[]? matches)
    {
        matches = null;
        if (RegexByteClass.RequiresUtf8ScalarMatch(
            atom.Kind,
            atom.Value.Span,
            options.Utf8,
            options.CaseInsensitive,
            options.UnicodeClasses))
        {
            return false;
        }

        bool[] set = new bool[256];
        bool any = false;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator))
            {
                set[value] = true;
                any = true;
            }
        }

        if (!any)
        {
            return false;
        }

        matches = set;
        return true;
    }

    private static bool TryReadLiteralByte(IReadOnlyList<RegexSyntaxNode> nodes, ref int index, byte expected)
    {
        if (index >= nodes.Count ||
            !TryGetLiteral(nodes[index], out byte[] literal) ||
            literal.Length != 1 ||
            literal[0] != expected)
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryGetLiteral(RegexSyntaxNode node, out byte[] literal)
    {
        literal = [];
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom ||
            atom.Value.Length == 0)
        {
            return false;
        }

        literal = atom.Value.ToArray();
        return true;
    }

    private static byte[]? TryGetCommonPrefix(List<RegexPathSemverCaptureBranch> branches)
    {
        if (branches.Count == 0)
        {
            return null;
        }

        int length = branches[0].SearchPrefix.Length;
        for (int index = 1; index < branches.Count; index++)
        {
            length = Math.Min(length, branches[index].SearchPrefix.Length);
        }

        int commonLength = 0;
        while (commonLength < length)
        {
            byte value = branches[0].SearchPrefix[commonLength];
            for (int index = 1; index < branches.Count; index++)
            {
                if (branches[index].SearchPrefix[commonLength] != value)
                {
                    return commonLength >= 2
                        ? branches[0].SearchPrefix.AsSpan(0, commonLength).ToArray()
                        : null;
                }
            }

            commonLength++;
        }

        return commonLength >= 2 ? branches[0].SearchPrefix.AsSpan(0, commonLength).ToArray() : null;
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
