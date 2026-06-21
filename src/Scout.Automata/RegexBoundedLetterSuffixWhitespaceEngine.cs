namespace Scout;

internal sealed class RegexBoundedLetterSuffixWhitespaceEngine
{
    private readonly byte[] suffix;
    private readonly int maximumPrefixLength;
    private readonly MemmemFinder suffixFinder;

    private RegexBoundedLetterSuffixWhitespaceEngine(byte[] suffix, int maximumPrefixLength)
    {
        this.suffix = suffix;
        this.maximumPrefixLength = maximumPrefixLength;
        suffixFinder = new MemmemFinder(suffix);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexBoundedLetterSuffixWhitespaceEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.Utf8 ||
            options.UnicodeClasses)
        {
            return false;
        }

        if (!TryUnwrapWithOptions(root, options, out root, out RegexCompileOptions rootOptions) ||
            root is not RegexSequenceNode sequence ||
            !TryCollectSequenceItems(sequence, rootOptions, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items))
        {
            return false;
        }

        int index = 0;
        if (!TryConsumeWhitespace(items, ref index) ||
            !TryConsumeRepeatedAsciiLetters(items, ref index, out int maximumPrefixLength) ||
            !TryConsumeAsciiLetterLiteralRun(items, ref index, out byte[] suffix) ||
            !TryConsumeWhitespace(items, ref index) ||
            index != items.Count ||
            suffix.Length == 0)
        {
            return false;
        }

        engine = new RegexBoundedLetterSuffixWhitespaceEngine(suffix, maximumPrefixLength);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int search = lowerBound;
        while (search <= haystack.Length - suffix.Length)
        {
            int relative = suffixFinder.Find(haystack[search..]);
            if (relative < 0)
            {
                return null;
            }

            int suffixStart = search + relative;
            if (TryBuildMatch(haystack, suffixStart, lowerBound, out RegexMatch match))
            {
                return match;
            }

            search = suffixStart + 1;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            !IsRegexWhitespace(haystack[start]))
        {
            return false;
        }

        int wordStart = start + 1;
        int wordEnd = wordStart;
        while (wordEnd < haystack.Length && IsAsciiLetter(haystack[wordEnd]))
        {
            wordEnd++;
        }

        int wordLength = wordEnd - wordStart;
        int prefixLength = wordLength - suffix.Length;
        if (prefixLength < 0 ||
            prefixLength > maximumPrefixLength ||
            wordEnd >= haystack.Length ||
            !IsRegexWhitespace(haystack[wordEnd]) ||
            !haystack.Slice(wordEnd - suffix.Length, suffix.Length).SequenceEqual(suffix))
        {
            return false;
        }

        length = wordEnd + 1 - start;
        return true;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (Find(haystack, offset) is RegexMatch match)
        {
            total += sumSpans ? match.Length : 1;
            offset = match.Length == 0
                ? Math.Min(match.End + 1, haystack.Length + 1)
                : match.End;
        }

        return total;
    }

    private bool TryBuildMatch(
        ReadOnlySpan<byte> haystack,
        int suffixStart,
        int lowerBound,
        out RegexMatch match)
    {
        match = default;
        int suffixEnd = suffixStart + suffix.Length;
        if (suffixEnd >= haystack.Length ||
            !IsRegexWhitespace(haystack[suffixEnd]))
        {
            return false;
        }

        int prefixStart = suffixStart;
        int prefixLength = 0;
        while (prefixStart > 0 && IsAsciiLetter(haystack[prefixStart - 1]))
        {
            prefixStart--;
            prefixLength++;
            if (prefixLength > maximumPrefixLength)
            {
                return false;
            }
        }

        if (prefixStart == 0 ||
            !IsRegexWhitespace(haystack[prefixStart - 1]))
        {
            return false;
        }

        int matchStart = prefixStart - 1;
        if (matchStart < lowerBound)
        {
            return false;
        }

        match = new RegexMatch(matchStart, suffixEnd + 1 - matchStart);
        return true;
    }

    private static bool TryConsumeWhitespace(List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items, ref int index)
    {
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive ||
            effectiveOptions.Utf8 ||
            effectiveOptions.UnicodeClasses ||
            unwrapped is not RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass })
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryConsumeRepeatedAsciiLetters(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        out int maximum)
    {
        maximum = 0;
        if (index >= items.Count ||
            !TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            effectiveOptions.CaseInsensitive ||
            effectiveOptions.Utf8 ||
            effectiveOptions.UnicodeClasses ||
            unwrapped is not RegexRepetitionNode
            {
                Minimum: 0,
                Maximum: { } actualMaximum,
                Lazy: false,
            } repetition ||
            !TryUnwrapWithOptions(repetition.Child, effectiveOptions, out RegexSyntaxNode child, out RegexCompileOptions childOptions) ||
            childOptions.CaseInsensitive ||
            childOptions.Utf8 ||
            childOptions.UnicodeClasses ||
            !IsAsciiLetterAtom(child, childOptions))
        {
            return false;
        }

        maximum = actualMaximum;
        index++;
        return true;
    }

    private static bool TryConsumeAsciiLetterLiteralRun(
        List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items,
        ref int index,
        out byte[] literal)
    {
        literal = [];
        var bytes = new List<byte>();
        while (index < items.Count &&
            TryUnwrapWithOptions(items[index].Node, items[index].Options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) &&
            !effectiveOptions.CaseInsensitive &&
            !effectiveOptions.Utf8 &&
            !effectiveOptions.UnicodeClasses &&
            unwrapped is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            ReadOnlySpan<byte> value = atom.Value.Span;
            for (int valueIndex = 0; valueIndex < value.Length; valueIndex++)
            {
                if (!IsAsciiLetter(value[valueIndex]))
                {
                    return false;
                }

                bytes.Add(value[valueIndex]);
            }

            index++;
        }

        literal = bytes.ToArray();
        return literal.Length > 0;
    }

    private static bool IsAsciiLetterAtom(RegexSyntaxNode node, RegexCompileOptions options)
    {
        if (node is not RegexAtomNode atom ||
            atom.Kind != RegexSyntaxKind.CharacterClass)
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
            if (matches != IsAsciiLetter((byte)value))
            {
                return false;
            }
        }

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

    private static bool IsAsciiLetter(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    private static bool IsRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or (byte)'\v';
    }
}
