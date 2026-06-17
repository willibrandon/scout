namespace Scout;

internal sealed class RegexDelimitedCaptureEngine
{
    private readonly RegexDelimitedCaptureField[] fields;
    private readonly byte delimiter;
    private readonly int captureCount;

    private RegexDelimitedCaptureEngine(RegexDelimitedCaptureField[] fields, byte delimiter, int captureCount)
    {
        this.fields = fields;
        this.delimiter = delimiter;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexDelimitedCaptureEngine? engine,
        bool compactFields = false)
    {
        engine = null;
        if (options.MultiLine)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode sequence ||
            sequence.Nodes.Count < 5 ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[0]) is not RegexAtomNode { Kind: RegexSyntaxKind.StartAnchor } ||
            UnwrapTransparentNonCapturingGroups(sequence.Nodes[^1]) is not RegexAtomNode { Kind: RegexSyntaxKind.EndAnchor })
        {
            return false;
        }

        var fields = new List<RegexDelimitedCaptureField>();
        byte? delimiter = null;
        int index = 1;
        while (index < sequence.Nodes.Count - 1)
        {
            if (!TryCreateField(sequence.Nodes[index], options, compactFields, out RegexDelimitedCaptureField? field))
            {
                return false;
            }

            fields.Add(field!);
            index++;
            if (index == sequence.Nodes.Count - 1)
            {
                break;
            }

            if (UnwrapTransparentNonCapturingGroups(sequence.Nodes[index]) is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } literal ||
                literal.Value.Length != 1)
            {
                return false;
            }

            byte separator = literal.Value.Span[0];
            if (delimiter.HasValue && delimiter.Value != separator)
            {
                return false;
            }

            delimiter = separator;
            index++;
        }

        if (!delimiter.HasValue || fields.Count == 0)
        {
            return false;
        }

        engine = new RegexDelimitedCaptureEngine(fields.ToArray(), delimiter.Value, captureCount);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out int length)
            ? new RegexMatch(0, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out _) ? 1 : 0;
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out int length) ? length : 0;
    }

    public long CountCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryMatchAt(haystack, startAt, out _) ? captureCount + 1L : 0;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int startAt, out int length)
    {
        length = 0;
        if (Math.Clamp(startAt, 0, haystack.Length) != 0 ||
            !TryMatchFields(haystack, groups: null))
        {
            return false;
        }

        length = haystack.Length;
        return true;
    }

    public RegexCaptures? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (Math.Clamp(startAt, 0, haystack.Length) != 0)
        {
            return null;
        }

        var groups = new RegexMatch?[captureCount + 1];
        if (!TryMatchFields(haystack, groups))
        {
            return null;
        }

        var match = new RegexMatch(0, haystack.Length);
        groups[0] = match;
        return new RegexCaptures(match, groups);
    }

    private bool TryMatchFields(ReadOnlySpan<byte> haystack, RegexMatch?[]? groups)
    {
        int position = 0;
        for (int index = 0; index < fields.Length; index++)
        {
            RegexDelimitedCaptureField field = fields[index];
            int fieldStart = position;
            int fieldEnd = index == fields.Length - 1
                ? haystack.Length
                : haystack[position..].IndexOf(delimiter);
            if (fieldEnd < 0)
            {
                return false;
            }

            if (index != fields.Length - 1)
            {
                fieldEnd += position;
            }

            int length = fieldEnd - fieldStart;
            if (length < field.Minimum || field.Maximum.HasValue && length > field.Maximum.Value)
            {
                return false;
            }

            for (int haystackIndex = fieldStart; haystackIndex < fieldEnd; haystackIndex++)
            {
                if (!field.Matches(haystack[haystackIndex]))
                {
                    return false;
                }
            }

            if (groups is not null)
            {
                groups[field.CaptureIndex] = new RegexMatch(fieldStart, length);
            }

            position = fieldEnd;
            if (index != fields.Length - 1)
            {
                position++;
            }
        }

        return position == haystack.Length;
    }

    private static bool TryCreateField(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        bool compactFields,
        out RegexDelimitedCaptureField? field)
    {
        field = null;
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexGroupNode { Kind: RegexSyntaxKind.CapturingGroup } group ||
            group.CaptureIndex <= 0 ||
            !string.IsNullOrEmpty(group.EnabledFlags) ||
            !string.IsNullOrEmpty(group.DisabledFlags))
        {
            return false;
        }

        RegexSyntaxNode child = UnwrapTransparentNonCapturingGroups(group.Child);
        int minimum = 1;
        int? maximum = 1;
        if (child is RegexRepetitionNode repetition)
        {
            minimum = repetition.Minimum;
            maximum = repetition.Maximum;
            child = UnwrapTransparentNonCapturingGroups(repetition.Child);
        }

        if (child is not RegexAtomNode atom ||
            RegexByteClass.RequiresUtf8ScalarMatch(atom.Kind, atom.Value.Span, options.Utf8, options.CaseInsensitive, options.UnicodeClasses))
        {
            return false;
        }

        if (compactFields &&
            RegexDelimitedCaptureField.TryCreateSpecialized(
                group.CaptureIndex,
                minimum,
                maximum,
                atom.Kind,
                atom.Value.Span,
                options,
                out field))
        {
            return true;
        }

        bool[] matches = new bool[256];
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
                matches[value] = true;
                any = true;
            }
        }

        if (!any)
        {
            return false;
        }

        field = new RegexDelimitedCaptureField(group.CaptureIndex, minimum, maximum, matches);
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
}
