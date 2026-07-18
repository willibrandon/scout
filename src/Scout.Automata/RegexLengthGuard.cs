namespace Scout;

/// <summary>
/// Rejects searches whose remaining byte length cannot satisfy the regex syntax.
/// </summary>
/// <param name="minimumBytes">The minimum number of bytes that any match can consume.</param>
/// <param name="maximumBytes">The maximum number of bytes that any match can consume.</param>
/// <param name="startAnchored">Whether the match must start at the beginning of the haystack.</param>
/// <param name="endAnchored">Whether the match must end at the end of the haystack.</param>
internal sealed class RegexLengthGuard(
    int minimumBytes,
    int maximumBytes,
    bool startAnchored,
    bool endAnchored)
{
    private const int Infinite = int.MaxValue;

    private readonly int _minimumBytes = minimumBytes;
    private readonly int _maximumBytes = maximumBytes;
    private readonly bool _startAnchored = startAnchored;
    private readonly bool _endAnchored = endAnchored;

    /// <summary>
    /// Gets the minimum number of bytes that any match can consume.
    /// </summary>
    internal int MinimumBytes => _minimumBytes;

    /// <summary>
    /// Creates a length guard when the syntax provides a useful bound.
    /// </summary>
    /// <param name="root">The parsed regex syntax.</param>
    /// <param name="options">The compilation options in effect at the root.</param>
    /// <returns>A length guard, or <see langword="null" /> when no useful bound is available.</returns>
    public static RegexLengthGuard? TryCreate(RegexSyntaxNode root, RegexCompileOptions options)
    {
        if (!TryAnalyze(root, options, out RegexLengthRange range))
        {
            return null;
        }

        bool hasMinimumGuard = range.MinimumBytes > 0;
        bool hasMaximumGuard = range.MaximumBytes != Infinite &&
            TryGetLeadingStartAnchor(root, options, out _) &&
            TryGetTrailingEndAnchor(root, options, out _);
        if (!hasMinimumGuard && !hasMaximumGuard)
        {
            return null;
        }

        return new RegexLengthGuard(
            range.MinimumBytes,
            range.MaximumBytes,
            hasMaximumGuard && TryGetLeadingStartAnchor(root, options, out _),
            hasMaximumGuard && TryGetTrailingEndAnchor(root, options, out _));
    }

    /// <summary>
    /// Determines whether the remaining haystack can satisfy the known length bounds.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns><see langword="true" /> when a match remains possible.</returns>
    public bool CanSearch(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (haystack.Length - startOffset < _minimumBytes)
        {
            return false;
        }

        if (_startAnchored && startOffset != 0)
        {
            return false;
        }

        return !_startAnchored ||
            !_endAnchored ||
            _maximumBytes == Infinite ||
            haystack.Length <= _maximumBytes;
    }

    private static bool TryAnalyze(RegexSyntaxNode node, RegexCompileOptions options, out RegexLengthRange range)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
            case RegexSyntaxKind.StartAnchor:
            case RegexSyntaxKind.EndAnchor:
            case RegexSyntaxKind.AbsoluteStartAnchor:
            case RegexSyntaxKind.AbsoluteEndAnchor:
            case RegexSyntaxKind.WordBoundary:
            case RegexSyntaxKind.NotWordBoundary:
            case RegexSyntaxKind.WordStartBoundary:
            case RegexSyntaxKind.WordEndBoundary:
            case RegexSyntaxKind.WordStartHalfBoundary:
            case RegexSyntaxKind.WordEndHalfBoundary:
            case RegexSyntaxKind.InlineFlags:
                range = RegexLengthRange.Zero;
                return true;

            case RegexSyntaxKind.Literal:
            case RegexSyntaxKind.Dot:
            case RegexSyntaxKind.AnyClass:
            case RegexSyntaxKind.UnicodePropertyClass:
            case RegexSyntaxKind.NotUnicodePropertyClass:
            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.ByteClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
            case RegexSyntaxKind.LetterClass:
            case RegexSyntaxKind.AlphanumericClass:
                return TryAnalyzeAtom((RegexAtomNode)node, options, out range);

            case RegexSyntaxKind.Sequence:
                return TryAnalyzeSequence((RegexSequenceNode)node, options, out range);

            case RegexSyntaxKind.Alternation:
                return TryAnalyzeAlternation((RegexAlternationNode)node, options, out range);

            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAnalyze(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), out range);

            case RegexSyntaxKind.Repetition:
                return TryAnalyzeRepetition((RegexRepetitionNode)node, options, out range);

            default:
                range = default;
                return false;
        }
    }

    private static bool TryAnalyzeSequence(RegexSequenceNode sequence, RegexCompileOptions options, out RegexLengthRange range)
    {
        int minimum = 0;
        int maximum = 0;
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = sequence.Nodes[index];
            if (node is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryAnalyze(node, currentOptions, out RegexLengthRange childRange))
            {
                range = default;
                return false;
            }

            minimum = SaturatingAdd(minimum, childRange.MinimumBytes);
            maximum = SaturatingAdd(maximum, childRange.MaximumBytes);
        }

        range = new RegexLengthRange(minimum, maximum);
        return true;
    }

    private static bool TryAnalyzeAlternation(RegexAlternationNode alternation, RegexCompileOptions options, out RegexLengthRange range)
    {
        int minimum = Infinite;
        int maximum = 0;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryAnalyze(alternation.Alternatives[index], options, out RegexLengthRange childRange))
            {
                range = default;
                return false;
            }

            minimum = Math.Min(minimum, childRange.MinimumBytes);
            maximum = Math.Max(maximum, childRange.MaximumBytes);
        }

        range = new RegexLengthRange(minimum == Infinite ? 0 : minimum, maximum);
        return true;
    }

    private static bool TryAnalyzeRepetition(RegexRepetitionNode repetition, RegexCompileOptions options, out RegexLengthRange range)
    {
        if (!TryAnalyze(repetition.Child, options, out RegexLengthRange childRange))
        {
            range = default;
            return false;
        }

        int minimum = SaturatingMultiply(childRange.MinimumBytes, repetition.Minimum);
        int maximum = repetition.Maximum.HasValue
            ? SaturatingMultiply(childRange.MaximumBytes, repetition.Maximum.Value)
            : childRange.MaximumBytes == 0 ? 0 : Infinite;
        range = new RegexLengthRange(minimum, maximum);
        return true;
    }

    private static bool TryAnalyzeAtom(RegexAtomNode atom, RegexCompileOptions options, out RegexLengthRange range)
    {
        ReadOnlySpan<byte> value = atom.Value.Span;
        if (RequiresUtf8LengthRange(atom.Kind, value, options))
        {
            range = new RegexLengthRange(1, 4);
            return true;
        }

        int length = atom.Kind == RegexSyntaxKind.Literal
            ? value.Length
            : 1;
        range = new RegexLengthRange(length, length);
        return true;
    }

    private static bool RequiresUtf8LengthRange(RegexSyntaxKind kind, ReadOnlySpan<byte> value, RegexCompileOptions options)
    {
        return RegexByteClass.RequiresUtf8ScalarMatch(
            kind,
            value,
            options.Utf8,
            options.CaseInsensitive,
            options.UnicodeClasses);
    }

    private static bool TryGetLeadingStartAnchor(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSyntaxKind anchorKind)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.StartAnchor when !options.MultiLine:
            case RegexSyntaxKind.AbsoluteStartAnchor:
                anchorKind = node.Kind;
                return true;

            case RegexSyntaxKind.Sequence:
                return TryGetLeadingStartAnchor((RegexSequenceNode)node, options, out anchorKind);

            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryGetLeadingStartAnchor(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out anchorKind);

            default:
                anchorKind = default;
                return false;
        }
    }

    private static bool TryGetLeadingStartAnchor(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out RegexSyntaxKind anchorKind)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = sequence.Nodes[index];
            if (node is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (node.Kind == RegexSyntaxKind.Empty)
            {
                continue;
            }

            return TryGetLeadingStartAnchor(node, currentOptions, out anchorKind);
        }

        anchorKind = default;
        return false;
    }

    private static bool TryGetTrailingEndAnchor(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSyntaxKind anchorKind)
    {
        switch (node.Kind)
        {
            case RegexSyntaxKind.EndAnchor when !options.MultiLine:
            case RegexSyntaxKind.AbsoluteEndAnchor:
                anchorKind = node.Kind;
                return true;

            case RegexSyntaxKind.Sequence:
                return TryGetTrailingEndAnchor((RegexSequenceNode)node, options, out anchorKind);

            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryGetTrailingEndAnchor(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out anchorKind);

            default:
                anchorKind = default;
                return false;
        }
    }

    private static bool TryGetTrailingEndAnchor(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out RegexSyntaxKind anchorKind)
    {
        RegexCompileOptions currentOptions = options;
        RegexSyntaxNode? lastNode = null;
        RegexCompileOptions lastOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = sequence.Nodes[index];
            if (node is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (node.Kind == RegexSyntaxKind.Empty)
            {
                continue;
            }

            lastNode = node;
            lastOptions = currentOptions;
        }

        if (lastNode is not null)
        {
            return TryGetTrailingEndAnchor(lastNode, lastOptions, out anchorKind);
        }

        anchorKind = default;
        return false;
    }

    private static int SaturatingAdd(int left, int right)
    {
        if (left == Infinite || right == Infinite || left > Infinite - right)
        {
            return Infinite;
        }

        return left + right;
    }

    private static int SaturatingMultiply(int value, int factor)
    {
        if (value == 0 || factor == 0)
        {
            return 0;
        }

        if (value == Infinite || value > Infinite / factor)
        {
            return Infinite;
        }

        return value * factor;
    }
}
