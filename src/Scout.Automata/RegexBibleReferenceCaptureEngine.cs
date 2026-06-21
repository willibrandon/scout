using System.Buffers;

namespace Scout;

internal sealed class RegexBibleReferenceCaptureEngine
{
    private const int BookCapture = 1;
    private const int BookPrefixCapture = 2;
    private const int BookPrefixTokenCapture = 3;
    private const int LocationsCapture = 4;
    private const int LocationCapture = 5;
    private const int ChapterCapture = 6;
    private const int ChapterBranchCapture = 7;
    private const int ChapterEndCapture = 8;
    private const int ChapterNextCapture = 9;
    private const int VerseGroupCapture = 10;
    private const int VerseCapture = 11;
    private const int VerseBranchCapture = 12;
    private const int VerseEndCapture = 13;
    private const int VerseNextCapture = 14;
    private static readonly SearchValues<byte> AsciiDigits = SearchValues.Create("0123456789"u8);
    private static readonly byte[] LetterProperty = [(byte)RegexUnicodePropertyKind.Letter];
    private static readonly byte[] SeparatorProperty = [(byte)RegexUnicodePropertyKind.Separator];

    private readonly RegexCompileOptions options;
    private readonly int captureCount;

    private RegexBibleReferenceCaptureEngine(RegexCompileOptions options, int captureCount)
    {
        this.options = options;
        this.captureCount = captureCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        int captureCount,
        out RegexBibleReferenceCaptureEngine? engine)
    {
        engine = null;
        if (captureCount != VerseNextCapture ||
            options.CaseInsensitive ||
            options.SwapGreed ||
            options.MultiLine ||
            options.DotMatchesNewline ||
            options.Utf8 ||
            !options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentNonCapturingGroups(root);
        if (root is not RegexSequenceNode { Nodes.Count: 3 } sequence ||
            !TryGetCapture(sequence.Nodes[0], BookCapture, "Book", out RegexGroupNode bookGroup) ||
            !IsBookExpression(bookGroup.Child) ||
            !IsBookSeparatorRun(sequence.Nodes[1], minimum: 1) ||
            !TryGetCapture(sequence.Nodes[2], LocationsCapture, "Locations", out RegexGroupNode locationsGroup) ||
            !IsLocationsExpression(locationsGroup.Child))
        {
            return false;
        }

        engine = new RegexBibleReferenceCaptureEngine(options, captureCount);
        return true;
    }

    public RegexCaptures? FindCaptures(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int search = lowerBound;
        while (search < haystack.Length)
        {
            int relative = haystack[search..].IndexOfAny(AsciiDigits);
            if (relative < 0)
            {
                return null;
            }

            int locationStart = search + relative;
            if (TryFindBookStartBeforeLocation(haystack, lowerBound, locationStart, out int matchStart) &&
                TryCreateCapturesFromStart(haystack, matchStart, out RegexCaptures? captures))
            {
                return captures;
            }

            search = locationStart + 1;
        }

        return null;
    }

    public bool TryCreateCaptures(ReadOnlySpan<byte> haystack, RegexMatch match, out RegexCaptures? captures)
    {
        captures = null;
        int start = match.Start;
        int end = match.End;
        if ((uint)start > (uint)haystack.Length ||
            (uint)end > (uint)haystack.Length ||
            start > end)
        {
            return false;
        }

        var groups = new RegexMatch?[captureCount + 1];
        groups[0] = match;

        if (!TryParseBook(haystack, start, end, groups, out int position))
        {
            return false;
        }

        int separatorStart = position;
        position = ConsumeBookSeparators(haystack, position, end);
        if (position == separatorStart)
        {
            return false;
        }

        int locationsStart = position;
        if (!TryParseLocations(haystack, position, end, groups, out int locationsCaptureEnd))
        {
            return false;
        }

        groups[LocationsCapture] = new RegexMatch(locationsStart, locationsCaptureEnd - locationsStart);
        captures = new RegexCaptures(match, groups);
        return true;
    }

    private bool TryCreateCapturesFromStart(ReadOnlySpan<byte> haystack, int start, out RegexCaptures? captures)
    {
        captures = null;
        var groups = new RegexMatch?[captureCount + 1];
        if (!TryParseBook(haystack, start, haystack.Length, groups, out int position))
        {
            return false;
        }

        int separatorStart = position;
        position = ConsumeBookSeparators(haystack, position, haystack.Length);
        if (position == separatorStart)
        {
            return false;
        }

        int locationsStart = position;
        if (!TryParseLocationsGreedy(haystack, position, groups, out int locationsCaptureEnd, out int matchEnd))
        {
            return false;
        }

        var match = new RegexMatch(start, matchEnd - start);
        groups[0] = match;
        groups[LocationsCapture] = new RegexMatch(locationsStart, locationsCaptureEnd - locationsStart);
        captures = new RegexCaptures(match, groups);
        return true;
    }

    private bool TryParseBook(
        ReadOnlySpan<byte> haystack,
        int start,
        int limit,
        RegexMatch?[] groups,
        out int position)
    {
        position = start;
        TryParseBookPrefix(haystack, limit, groups, ref position);

        int letterStart = position;
        while (TryLetterMatchLength(haystack, position, limit, out int length))
        {
            position += length;
        }

        if (position == letterStart)
        {
            return false;
        }

        if (position < limit && haystack[position] == (byte)'.')
        {
            position++;
        }

        groups[BookCapture] = new RegexMatch(start, position - start);
        return true;
    }

    private void TryParseBookPrefix(
        ReadOnlySpan<byte> haystack,
        int limit,
        RegexMatch?[] groups,
        ref int position)
    {
        int prefixStart = position;
        if (position >= limit)
        {
            return;
        }

        int tokenEnd;
        byte first = haystack[position];
        if (first is >= (byte)'1' and <= (byte)'4')
        {
            tokenEnd = position + 1;
        }
        else if (first == (byte)'I')
        {
            tokenEnd = position + 1;
            while (tokenEnd < limit &&
                tokenEnd - position < 4 &&
                haystack[tokenEnd] == (byte)'I')
            {
                tokenEnd++;
            }
        }
        else
        {
            return;
        }

        int afterSeparators = ConsumeBookSeparators(haystack, tokenEnd, limit);
        if (!TryLetterMatchLength(haystack, afterSeparators, limit, out _))
        {
            return;
        }

        groups[BookPrefixCapture] = new RegexMatch(prefixStart, afterSeparators - prefixStart);
        groups[BookPrefixTokenCapture] = new RegexMatch(prefixStart, tokenEnd - prefixStart);
        position = afterSeparators;
    }

    private bool TryParseLocations(
        ReadOnlySpan<byte> haystack,
        int position,
        int limit,
        RegexMatch?[] groups,
        out int locationsCaptureEnd)
    {
        locationsCaptureEnd = position;
        bool sawLocation = false;
        while (position < limit)
        {
            int locationStart = position;
            if (!TryParseLocation(haystack, position, limit, groups, out int locationCaptureEnd, out position))
            {
                return false;
            }

            sawLocation = true;
            locationsCaptureEnd = locationCaptureEnd;
            groups[LocationCapture] = new RegexMatch(locationStart, locationCaptureEnd - locationStart);
        }

        return sawLocation && position == limit;
    }

    private bool TryParseLocationsGreedy(
        ReadOnlySpan<byte> haystack,
        int position,
        RegexMatch?[] groups,
        out int locationsCaptureEnd,
        out int matchEnd)
    {
        locationsCaptureEnd = position;
        matchEnd = position;
        bool sawLocation = false;
        while (position < haystack.Length)
        {
            int locationStart = position;
            if (!TryParseLocation(haystack, position, haystack.Length, groups, out int locationCaptureEnd, out int nextPosition))
            {
                break;
            }

            sawLocation = true;
            locationsCaptureEnd = locationCaptureEnd;
            groups[LocationCapture] = new RegexMatch(locationStart, locationCaptureEnd - locationStart);
            position = nextPosition;
        }

        matchEnd = position;
        return sawLocation;
    }

    private bool TryParseLocation(
        ReadOnlySpan<byte> haystack,
        int position,
        int limit,
        RegexMatch?[] groups,
        out int locationCaptureEnd,
        out int nextPosition)
    {
        locationCaptureEnd = position;
        nextPosition = position;

        int chapterStart = position;
        if (!TryConsumeChapter(haystack, position, limit, out position))
        {
            return false;
        }

        groups[ChapterCapture] = new RegexMatch(chapterStart, position - chapterStart);

        while (TryParseChapterBranch(haystack, position, limit, groups, out int branchEnd))
        {
            position = branchEnd;
        }

        TryParseVerse(haystack, ref position, limit, groups);

        while (TryParseVerseBranch(haystack, position, limit, groups, out int branchEnd))
        {
            position = branchEnd;
        }

        if (TryWhitespaceMatchLength(haystack, position, limit, out int trailingWhitespaceLength))
        {
            position += trailingWhitespaceLength;
        }

        locationCaptureEnd = position;
        nextPosition = position;
        return true;
    }

    private static bool TryConsumeChapter(ReadOnlySpan<byte> haystack, int position, int limit, out int end)
    {
        end = position;
        if (position >= limit || !IsAsciiDigit(haystack[position]))
        {
            return false;
        }

        int available = 1;
        while (position + available < limit &&
            available < 3 &&
            IsAsciiDigit(haystack[position + available]))
        {
            available++;
        }

        int length = haystack[position] == (byte)'1'
            ? available
            : Math.Min(available, 2);
        end = position + length;
        return true;
    }

    private bool TryParseChapterBranch(
        ReadOnlySpan<byte> haystack,
        int position,
        int limit,
        RegexMatch?[] groups,
        out int end)
    {
        end = position;
        if (position >= limit)
        {
            return false;
        }

        if (haystack[position] == (byte)'-')
        {
            int digitStart = position + 1;
            if (!TryConsumeRegexDigitRun(haystack, digitStart, limit, out int digitEnd))
            {
                return false;
            }

            groups[ChapterBranchCapture] = new RegexMatch(position, digitEnd - position);
            groups[ChapterEndCapture] = new RegexMatch(digitStart, digitEnd - digitStart);
            end = digitEnd;
            return true;
        }

        if (haystack[position] != (byte)',')
        {
            return false;
        }

        int nextStart = ConsumeWhitespace(haystack, position + 1, limit);
        if (nextStart >= limit || haystack[nextStart] != (byte)'\\')
        {
            return false;
        }

        int dStart = nextStart + 1;
        int nextEnd = dStart;
        while (nextEnd < limit && haystack[nextEnd] == (byte)'d')
        {
            nextEnd++;
        }

        if (nextEnd == dStart)
        {
            return false;
        }

        groups[ChapterBranchCapture] = new RegexMatch(position, nextEnd - position);
        groups[ChapterNextCapture] = new RegexMatch(nextStart, nextEnd - nextStart);
        end = nextEnd;
        return true;
    }

    private void TryParseVerse(
        ReadOnlySpan<byte> haystack,
        ref int position,
        int limit,
        RegexMatch?[] groups)
    {
        if (position >= limit || haystack[position] != (byte)':')
        {
            return;
        }

        int digitStart = ConsumeWhitespace(haystack, position + 1, limit);
        if (!TryConsumeRegexDigitRun(haystack, digitStart, limit, out int digitEnd))
        {
            return;
        }

        groups[VerseGroupCapture] = new RegexMatch(position, digitEnd - position);
        groups[VerseCapture] = new RegexMatch(digitStart, digitEnd - digitStart);
        position = digitEnd;
    }

    private bool TryParseVerseBranch(
        ReadOnlySpan<byte> haystack,
        int position,
        int limit,
        RegexMatch?[] groups,
        out int end)
    {
        end = position;
        if (position >= limit)
        {
            return false;
        }

        if (haystack[position] == (byte)'-')
        {
            int verseEndStart = position + 1;
            if (!TryConsumeRegexDigitRun(haystack, verseEndStart, limit, out int verseEndEnd))
            {
                return false;
            }

            groups[VerseBranchCapture] = new RegexMatch(position, verseEndEnd - position);
            groups[VerseEndCapture] = new RegexMatch(verseEndStart, verseEndEnd - verseEndStart);
            end = verseEndEnd;
            return true;
        }

        if (haystack[position] != (byte)',')
        {
            return false;
        }

        int verseNextStart = ConsumeWhitespace(haystack, position + 1, limit);
        if (!TryConsumeRegexDigitRun(haystack, verseNextStart, limit, out int verseNextEnd))
        {
            return false;
        }

        groups[VerseBranchCapture] = new RegexMatch(position, verseNextEnd - position);
        groups[VerseNextCapture] = new RegexMatch(verseNextStart, verseNextEnd - verseNextStart);
        end = verseNextEnd;
        return true;
    }

    private bool TryConsumeRegexDigitRun(ReadOnlySpan<byte> haystack, int position, int limit, out int end)
    {
        end = position;
        while (TryDigitMatchLength(haystack, end, limit, out int length))
        {
            end += length;
        }

        return end > position;
    }

    private bool TryFindBookStartBeforeLocation(
        ReadOnlySpan<byte> haystack,
        int lowerBound,
        int locationStart,
        out int matchStart)
    {
        matchStart = 0;
        if (!TryBookSeparatorEndingAt(haystack, lowerBound, locationStart, out int bookEnd))
        {
            return false;
        }

        while (TryBookSeparatorEndingAt(haystack, lowerBound, bookEnd, out int separatorStart))
        {
            bookEnd = separatorStart;
        }

        int letterEnd = bookEnd;
        if (letterEnd > lowerBound && haystack[letterEnd - 1] == (byte)'.')
        {
            letterEnd--;
        }

        int letterStart = letterEnd;
        while (TryLetterEndingAt(haystack, lowerBound, letterStart, out int previousLetterStart))
        {
            letterStart = previousLetterStart;
        }

        if (letterStart == letterEnd)
        {
            return false;
        }

        int prefixEnd = letterStart;
        while (TryBookSeparatorEndingAt(haystack, lowerBound, prefixEnd, out int separatorStart))
        {
            prefixEnd = separatorStart;
        }

        matchStart = TryBookPrefixTokenEndingAt(haystack, lowerBound, prefixEnd, out int prefixStart)
            ? prefixStart
            : letterStart;
        return matchStart >= lowerBound;
    }

    private static int ConsumeBookSeparators(ReadOnlySpan<byte> haystack, int position, int limit)
    {
        while (TryBookSeparatorMatchLength(haystack, position, limit, out int length))
        {
            position += length;
        }

        return position;
    }

    private bool TryLetterEndingAt(ReadOnlySpan<byte> haystack, int lowerBound, int end, out int start)
    {
        start = 0;
        if (end <= lowerBound)
        {
            return false;
        }

        byte previous = haystack[end - 1];
        if (previous <= 0x7F)
        {
            if (!IsAsciiLetter(previous))
            {
                return false;
            }

            start = end - 1;
            return true;
        }

        int firstCandidate = Math.Max(lowerBound, end - 4);
        for (int candidate = firstCandidate; candidate < end; candidate++)
        {
            if (TryLetterMatchLength(haystack, candidate, end, out int length) &&
                candidate + length == end)
            {
                start = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryBookSeparatorEndingAt(ReadOnlySpan<byte> haystack, int lowerBound, int end, out int start)
    {
        start = 0;
        if (end <= lowerBound)
        {
            return false;
        }

        byte previous = haystack[end - 1];
        if (previous <= 0x7F)
        {
            if (!IsAsciiBookSeparator(previous))
            {
                return false;
            }

            start = end - 1;
            return true;
        }

        int firstCandidate = Math.Max(lowerBound, end - 4);
        for (int candidate = firstCandidate; candidate < end; candidate++)
        {
            if (TryBookSeparatorMatchLength(haystack, candidate, end, out int length) &&
                candidate + length == end)
            {
                start = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryBookPrefixTokenEndingAt(ReadOnlySpan<byte> haystack, int lowerBound, int end, out int start)
    {
        start = 0;
        if (end <= lowerBound)
        {
            return false;
        }

        byte previous = haystack[end - 1];
        if (previous is >= (byte)'1' and <= (byte)'4')
        {
            start = end - 1;
            return true;
        }

        if (previous != (byte)'I')
        {
            return false;
        }

        start = end - 1;
        while (start > lowerBound &&
            end - start < 4 &&
            haystack[start - 1] == (byte)'I')
        {
            start--;
        }

        return true;
    }

    private static int ConsumeWhitespace(ReadOnlySpan<byte> haystack, int position, int limit)
    {
        while (TryWhitespaceMatchLength(haystack, position, limit, out int length))
        {
            position += length;
        }

        return position;
    }

    private bool TryLetterMatchLength(ReadOnlySpan<byte> haystack, int position, int limit, out int length)
    {
        length = 0;
        return position < limit &&
            RegexByteClass.TryGetAtomMatchLength(
                haystack,
                position,
                RegexSyntaxKind.UnicodePropertyClass,
                LetterProperty,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                options.UnicodeClasses,
                out length) &&
            position + length <= limit;
    }

    private bool TryDigitMatchLength(ReadOnlySpan<byte> haystack, int position, int limit, out int length)
    {
        length = 0;
        return position < limit &&
            RegexByteClass.TryGetAtomMatchLength(
                haystack,
                position,
                RegexSyntaxKind.DigitClass,
                ReadOnlySpan<byte>.Empty,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                options.UnicodeClasses,
                out length) &&
            position + length <= limit;
    }

    private static bool TryBookSeparatorMatchLength(ReadOnlySpan<byte> haystack, int position, int limit, out int length)
    {
        length = 0;
        if (position >= limit)
        {
            return false;
        }

        byte value = haystack[position];
        if (value is (byte)'\t' or (byte)'\f')
        {
            length = 1;
            return true;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            RegexSyntaxKind.UnicodePropertyClass,
            SeparatorProperty,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: true,
            out length) &&
            position + length <= limit;
    }

    private static bool TryWhitespaceMatchLength(ReadOnlySpan<byte> haystack, int position, int limit, out int length)
    {
        length = 0;
        if (position >= limit)
        {
            return false;
        }

        byte value = haystack[position];
        if (value <= 0x7F)
        {
            if (!IsAsciiRegexWhitespace(value))
            {
                return false;
            }

            length = 1;
            return true;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            RegexSyntaxKind.WhitespaceClass,
            ReadOnlySpan<byte>.Empty,
            caseInsensitive: false,
            multiLine: false,
            dotMatchesNewline: false,
            crlf: false,
            lineTerminator: (byte)'\n',
            utf8: false,
            unicodeClasses: true,
            out length) &&
            position + length <= limit;
    }

    private static bool IsBookExpression(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsOptionalBookPrefix(sequence.Nodes[0]) &&
            IsOneOrMoreUnicodeProperty(sequence.Nodes[1], RegexUnicodePropertyKind.Letter) &&
            IsOptionalLiteral(sequence.Nodes[2], (byte)'.');
    }

    private static bool IsOptionalBookPrefix(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: 1,
            Lazy: false,
        } repetition &&
            TryGetCapture(repetition.Child, BookPrefixCapture, captureName: null, out RegexGroupNode prefixGroup) &&
            UnwrapTransparentNonCapturingGroups(prefixGroup.Child) is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            TryGetCapture(sequence.Nodes[0], BookPrefixTokenCapture, captureName: null, out RegexGroupNode prefixTokenGroup) &&
            IsBookPrefixToken(prefixTokenGroup.Child) &&
            IsBookSeparatorRun(sequence.Nodes[1], minimum: 0);
    }

    private static bool IsBookPrefixToken(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAlternationNode { Alternatives.Count: 2 } alternation)
        {
            return false;
        }

        bool sawDigit = false;
        bool sawRoman = false;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            RegexSyntaxNode alternative = UnwrapTransparentNonCapturingGroups(alternation.Alternatives[index]);
            sawDigit |= IsCharacterClassExpression(alternative, "1234"u8);
            sawRoman |= IsLiteralRepetition(alternative, (byte)'I', minimum: 1, maximum: 4);
        }

        return sawDigit && sawRoman;
    }

    private static bool IsLocationsExpression(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 1,
            Maximum: null,
            Lazy: false,
        } repetition &&
            TryGetCapture(repetition.Child, LocationCapture, captureName: null, out RegexGroupNode locationGroup) &&
            IsLocationExpression(locationGroup.Child);
    }

    private static bool IsLocationExpression(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexSequenceNode { Nodes.Count: 5 } sequence &&
            IsChapterCapture(sequence.Nodes[0]) &&
            IsChapterBranchRepetition(sequence.Nodes[1]) &&
            IsOptionalVerse(sequence.Nodes[2]) &&
            IsVerseBranchRepetition(sequence.Nodes[3]) &&
            IsOptionalWhitespace(sequence.Nodes[4]);
    }

    private static bool IsChapterCapture(RegexSyntaxNode node)
    {
        return TryGetCapture(node, ChapterCapture, "Chapter", out RegexGroupNode chapterGroup) &&
            UnwrapTransparentNonCapturingGroups(chapterGroup.Child) is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsOptionalLiteral(sequence.Nodes[0], (byte)'1') &&
            IsOptionalAsciiDigitClass(sequence.Nodes[1]) &&
            IsAsciiDigitClass(sequence.Nodes[2]);
    }

    private static bool IsChapterBranchRepetition(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: null,
            Lazy: false,
        } repetition &&
            TryGetCapture(repetition.Child, ChapterBranchCapture, captureName: null, out RegexGroupNode branchGroup) &&
            IsChapterBranchAlternation(branchGroup.Child);
    }

    private static bool IsChapterBranchAlternation(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAlternationNode { Alternatives.Count: 2 } alternation)
        {
            return false;
        }

        bool sawEnd = false;
        bool sawNext = false;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            RegexSyntaxNode alternative = UnwrapTransparentNonCapturingGroups(alternation.Alternatives[index]);
            sawEnd |= IsHyphenDigitCapture(alternative, ChapterEndCapture, "ChapterEnd");
            sawNext |= IsCommaBackslashDRunCapture(alternative, ChapterNextCapture, "ChapterNext");
        }

        return sawEnd && sawNext;
    }

    private static bool IsOptionalVerse(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: 1,
            Lazy: false,
        } repetition &&
            TryGetCapture(repetition.Child, VerseGroupCapture, captureName: null, out RegexGroupNode verseGroup) &&
            UnwrapTransparentNonCapturingGroups(verseGroup.Child) is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsLiteral(sequence.Nodes[0], (byte)':') &&
            IsWhitespaceRun(sequence.Nodes[1], minimum: 0) &&
            IsDigitRunCapture(sequence.Nodes[2], VerseCapture, "Verse");
    }

    private static bool IsVerseBranchRepetition(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: null,
            Lazy: false,
        } repetition &&
            TryGetCapture(repetition.Child, VerseBranchCapture, captureName: null, out RegexGroupNode branchGroup) &&
            IsVerseBranchAlternation(branchGroup.Child);
    }

    private static bool IsVerseBranchAlternation(RegexSyntaxNode node)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is not RegexAlternationNode { Alternatives.Count: 2 } alternation)
        {
            return false;
        }

        bool sawEnd = false;
        bool sawNext = false;
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            RegexSyntaxNode alternative = UnwrapTransparentNonCapturingGroups(alternation.Alternatives[index]);
            sawEnd |= IsHyphenDigitCapture(alternative, VerseEndCapture, "VerseEnd");
            sawNext |= IsCommaDigitCapture(alternative, VerseNextCapture, "VerseNext");
        }

        return sawEnd && sawNext;
    }

    private static bool IsHyphenDigitCapture(RegexSyntaxNode node, int captureIndex, string captureName)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            IsLiteral(sequence.Nodes[0], (byte)'-') &&
            IsDigitRunCapture(sequence.Nodes[1], captureIndex, captureName);
    }

    private static bool IsCommaDigitCapture(RegexSyntaxNode node, int captureIndex, string captureName)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsLiteral(sequence.Nodes[0], (byte)',') &&
            IsWhitespaceRun(sequence.Nodes[1], minimum: 0) &&
            IsDigitRunCapture(sequence.Nodes[2], captureIndex, captureName);
    }

    private static bool IsCommaBackslashDRunCapture(RegexSyntaxNode node, int captureIndex, string captureName)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexSequenceNode { Nodes.Count: 3 } sequence &&
            IsLiteral(sequence.Nodes[0], (byte)',') &&
            IsWhitespaceRun(sequence.Nodes[1], minimum: 0) &&
            TryGetCapture(sequence.Nodes[2], captureIndex, captureName, out RegexGroupNode captureGroup) &&
            IsBackslashDRun(captureGroup.Child);
    }

    private static bool IsBackslashDRun(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexSequenceNode { Nodes.Count: 2 } sequence &&
            IsLiteral(sequence.Nodes[0], (byte)'\\') &&
            IsLiteralRepetition(sequence.Nodes[1], (byte)'d', minimum: 1, maximum: null);
    }

    private static bool IsDigitRunCapture(RegexSyntaxNode node, int captureIndex, string captureName)
    {
        return TryGetCapture(node, captureIndex, captureName, out RegexGroupNode captureGroup) &&
            IsDigitRun(captureGroup.Child);
    }

    private static bool IsDigitRun(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 1,
            Maximum: null,
            Lazy: false,
        } repetition &&
            UnwrapTransparentNonCapturingGroups(repetition.Child) is RegexAtomNode { Kind: RegexSyntaxKind.DigitClass };
    }

    private static bool IsOptionalWhitespace(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: 1,
            Lazy: false,
        } repetition &&
            UnwrapTransparentNonCapturingGroups(repetition.Child) is RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass };
    }

    private static bool IsBookSeparatorRun(RegexSyntaxNode node, int minimum)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: var actualMinimum,
            Maximum: null,
            Lazy: false,
        } repetition &&
            actualMinimum == minimum &&
            IsCharacterClassExpression(repetition.Child, "\\t\\f\\pZ"u8);
    }

    private static bool IsWhitespaceRun(RegexSyntaxNode node, int minimum)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: var actualMinimum,
            Maximum: null,
            Lazy: false,
        } repetition &&
            actualMinimum == minimum &&
            UnwrapTransparentNonCapturingGroups(repetition.Child) is RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass };
    }

    private static bool IsOneOrMoreUnicodeProperty(RegexSyntaxNode node, RegexUnicodePropertyKind property)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 1,
            Maximum: null,
            Lazy: false,
        } repetition &&
            IsUnicodeProperty(repetition.Child, property);
    }

    private static bool IsUnicodeProperty(RegexSyntaxNode node, RegexUnicodePropertyKind property)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexAtomNode
        {
            Kind: RegexSyntaxKind.UnicodePropertyClass,
            Value.Length: 1,
        } atom && atom.Value.Span[0] == (byte)property;
    }

    private static bool IsOptionalLiteral(RegexSyntaxNode node, byte literal)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: 1,
            Lazy: false,
        } repetition &&
            IsLiteral(repetition.Child, literal);
    }

    private static bool IsLiteralRepetition(RegexSyntaxNode node, byte literal, int minimum, int? maximum)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: var actualMinimum,
            Maximum: var actualMaximum,
            Lazy: false,
        } repetition &&
            actualMinimum == minimum &&
            actualMaximum == maximum &&
            IsLiteral(repetition.Child, literal);
    }

    private static bool IsOptionalAsciiDigitClass(RegexSyntaxNode node)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexRepetitionNode
        {
            Minimum: 0,
            Maximum: 1,
            Lazy: false,
        } repetition &&
            IsAsciiDigitClass(repetition.Child);
    }

    private static bool IsAsciiDigitClass(RegexSyntaxNode node)
    {
        return IsCharacterClassExpression(node, "0-9"u8);
    }

    private static bool IsCharacterClassExpression(RegexSyntaxNode node, ReadOnlySpan<byte> expression)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexAtomNode
        {
            Kind: RegexSyntaxKind.CharacterClass,
        } atom && atom.Value.Span.SequenceEqual(expression);
    }

    private static bool IsLiteral(RegexSyntaxNode node, byte literal)
    {
        return UnwrapTransparentNonCapturingGroups(node) is RegexAtomNode
        {
            Kind: RegexSyntaxKind.Literal,
            Value.Length: 1,
        } atom && atom.Value.Span[0] == literal;
    }

    private static bool TryGetCapture(
        RegexSyntaxNode node,
        int captureIndex,
        string? captureName,
        out RegexGroupNode group)
    {
        node = UnwrapTransparentNonCapturingGroups(node);
        if (node is RegexGroupNode
            {
                Kind: RegexSyntaxKind.CapturingGroup,
                EnabledFlags.Length: 0,
                DisabledFlags.Length: 0,
            } candidate &&
            candidate.CaptureIndex == captureIndex &&
            string.Equals(candidate.CaptureName, captureName, StringComparison.Ordinal))
        {
            group = candidate;
            return true;
        }

        group = null!;
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

    private static bool IsAsciiDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsAsciiLetter(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z';
    }

    private static bool IsAsciiBookSeparator(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\f';
    }

    private static bool IsAsciiRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }
}
