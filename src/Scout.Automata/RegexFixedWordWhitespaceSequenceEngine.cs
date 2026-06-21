using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexFixedWordWhitespaceSequenceEngine
{
    private const int MaxTokens = 64;
    private const int MaxRepeat = 256;
    private const int MaxUtf8ScalarBytes = 4;
    private const byte WordKind = 0;
    private const byte WhitespaceKind = 1;
    private static readonly Vector128<byte> SpaceVector128 = Vector128.Create((byte)' ');
    private static readonly Vector128<byte> TabVector128 = Vector128.Create((byte)'\t');
    private static readonly Vector128<byte> LineFeedVector128 = Vector128.Create((byte)'\n');
    private static readonly Vector128<byte> VerticalTabVector128 = Vector128.Create((byte)0x0b);
    private static readonly Vector128<byte> FormFeedVector128 = Vector128.Create((byte)'\f');
    private static readonly Vector128<byte> CarriageReturnVector128 = Vector128.Create((byte)'\r');
    private static readonly Vector256<byte> SpaceVector256 = Vector256.Create((byte)' ');
    private static readonly Vector256<byte> TabVector256 = Vector256.Create((byte)'\t');
    private static readonly Vector256<byte> LineFeedVector256 = Vector256.Create((byte)'\n');
    private static readonly Vector256<byte> VerticalTabVector256 = Vector256.Create((byte)0x0b);
    private static readonly Vector256<byte> FormFeedVector256 = Vector256.Create((byte)'\f');
    private static readonly Vector256<byte> CarriageReturnVector256 = Vector256.Create((byte)'\r');

    private readonly byte[] tokenKinds;
    private readonly int[] tokenCounts;
    private readonly RegexCompileOptions options;
    private readonly int fixedAsciiLength;
    private readonly int anchorPrefixLength;
    private readonly bool hasThreeWordWhitespaceShape;
    private readonly int firstWordCount;
    private readonly int firstWhitespaceCount;
    private readonly int secondWordCount;
    private readonly int secondWhitespaceCount;
    private readonly int thirdWordCount;

    private RegexFixedWordWhitespaceSequenceEngine(
        byte[] tokenKinds,
        int[] tokenCounts,
        RegexCompileOptions options,
        int fixedAsciiLength,
        int anchorPrefixLength,
        bool hasThreeWordWhitespaceShape,
        int firstWordCount,
        int firstWhitespaceCount,
        int secondWordCount,
        int secondWhitespaceCount,
        int thirdWordCount)
    {
        this.tokenKinds = tokenKinds;
        this.tokenCounts = tokenCounts;
        this.options = options;
        this.fixedAsciiLength = fixedAsciiLength;
        this.anchorPrefixLength = anchorPrefixLength;
        this.hasThreeWordWhitespaceShape = hasThreeWordWhitespaceShape;
        this.firstWordCount = firstWordCount;
        this.firstWhitespaceCount = firstWhitespaceCount;
        this.secondWordCount = secondWordCount;
        this.secondWhitespaceCount = secondWhitespaceCount;
        this.thirdWordCount = thirdWordCount;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexFixedWordWhitespaceSequenceEngine? engine)
    {
        engine = null;
        if (options.CaseInsensitive ||
            options.SwapGreed ||
            options.Utf8)
        {
            return false;
        }

        if (!TryCollectTokens(root, out List<byte>? collectedKinds, out List<int>? collectedCounts) ||
            collectedKinds is null ||
            collectedCounts is null ||
            collectedKinds.Count < 3 ||
            collectedKinds.Count > MaxTokens)
        {
            return false;
        }

        bool sawWord = false;
        bool sawWhitespace = false;
        int fixedAsciiLength = 0;
        int anchorPrefixLength = 0;
        bool foundAnchor = false;
        for (int index = 0; index < collectedKinds.Count; index++)
        {
            byte tokenKind = collectedKinds[index];
            int tokenCount = collectedCounts[index];
            sawWord |= tokenKind == WordKind;
            sawWhitespace |= tokenKind == WhitespaceKind;
            if (!foundAnchor && tokenKind == WhitespaceKind)
            {
                anchorPrefixLength = fixedAsciiLength;
                foundAnchor = true;
            }

            fixedAsciiLength += tokenCount;
        }

        if (!sawWord || !sawWhitespace || !foundAnchor || fixedAsciiLength <= 0)
        {
            return false;
        }

        bool hasThreeWordWhitespaceShape = IsThreeWordWhitespaceShape(collectedKinds, collectedCounts);
        if (options.UnicodeClasses &&
            (!hasThreeWordWhitespaceShape ||
             collectedCounts[1] != 1))
        {
            return false;
        }

        engine = new RegexFixedWordWhitespaceSequenceEngine(
            [.. collectedKinds],
            [.. collectedCounts],
            options,
            fixedAsciiLength,
            anchorPrefixLength,
            hasThreeWordWhitespaceShape,
            collectedCounts[0],
            collectedCounts.Count == 5 ? collectedCounts[1] : 0,
            collectedCounts.Count == 5 ? collectedCounts[2] : 0,
            collectedCounts.Count == 5 ? collectedCounts[3] : 0,
            collectedCounts.Count == 5 ? collectedCounts[4] : 0);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CanUseAsciiFastPath(haystack)
            ? FindAscii(haystack, startAt)
            : FindGeneric(haystack, startAt);
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
        return CanUseAsciiFastPath(haystack)
            ? TryMatchAsciiAt(haystack, start, out length)
            : TryMatchGenericAt(haystack, start, out length);
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long count = 0;
        long spanSum = 0;
        if (!options.UnicodeClasses)
        {
            CountOrSumAscii(haystack, startAt, sumSpans, ref count, ref spanSum);
        }
        else if (!ContainsNonAscii(haystack))
        {
            CountOrSumAscii(haystack, startAt, sumSpans, ref count, ref spanSum);
        }
        else if (hasThreeWordWhitespaceShape && firstWhitespaceCount == 1)
        {
            CountOrSumMostlyAsciiUnicode(haystack, startAt, sumSpans, ref count, ref spanSum);
        }
        else
        {
            CountOrSumGeneric(haystack, startAt, sumSpans, ref count, ref spanSum);
        }

        return sumSpans ? spanSum : count;
    }

    private void CountOrSumMostlyAsciiUnicode(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        var guardRanges = new List<Range>();
        // A match that needs Unicode scalar semantics must include a non-ASCII scalar.
        // Guard one fixed pattern width around each such scalar and run the generic
        // matcher there; pure ASCII spans between guards can keep the vector fast path.
        int guardRadius = fixedAsciiLength + MaxUtf8ScalarBytes;
        int search = 0;
        while (search < haystack.Length)
        {
            int relative = haystack[search..].IndexOfAnyExceptInRange((byte)0, (byte)0x7F);
            if (relative < 0)
            {
                break;
            }

            int index = search + relative;
            int start = Math.Max(0, index - guardRadius);
            int end = Math.Min(haystack.Length, index + guardRadius + 1);
            if (guardRanges.Count != 0 && start <= guardRanges[^1].End.Value)
            {
                Range previous = guardRanges[^1];
                guardRanges[^1] = previous.Start.Value..Math.Max(previous.End.Value, end);
                search = index + 1;
                continue;
            }

            guardRanges.Add(start..end);
            search = index + 1;
        }

        int segmentStart = Math.Clamp(startAt, 0, haystack.Length);
        for (int index = 0; index < guardRanges.Count; index++)
        {
            Range guard = guardRanges[index];
            int guardStart = Math.Max(segmentStart, guard.Start.Value);
            int guardEnd = Math.Max(guardStart, guard.End.Value);
            if (segmentStart < guardStart)
            {
                CountOrSumThreeWordWhitespaceAsciiRange(
                    haystack,
                    segmentStart,
                    guardStart,
                    sumSpans,
                    ref count,
                    ref spanSum);
            }

            segmentStart = CountOrSumGenericRange(
                haystack,
                guardStart,
                guardEnd,
                sumSpans,
                ref count,
                ref spanSum);
        }

        if (segmentStart < haystack.Length)
        {
            CountOrSumThreeWordWhitespaceAsciiRange(
                haystack,
                segmentStart,
                haystack.Length,
                sumSpans,
                ref count,
                ref spanSum);
        }
    }

    private RegexMatch? FindAscii(ReadOnlySpan<byte> haystack, int startAt)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int search = offset + anchorPrefixLength;
        int lastAnchor = haystack.Length - (fixedAsciiLength - anchorPrefixLength);
        while (search <= lastAnchor)
        {
            int anchor = FindNextAsciiWhitespace(haystack, search, lastAnchor);
            if (anchor < 0)
            {
                return null;
            }

            int start = anchor - anchorPrefixLength;
            bool matched = hasThreeWordWhitespaceShape && firstWhitespaceCount == 1
                ? TryMatchThreeWordWhitespaceAsciiAtKnownFirstWhitespace(haystack, start)
                : TryMatchAsciiAt(haystack, start, out _);
            if (matched)
            {
                return new RegexMatch(start, fixedAsciiLength);
            }

            search = anchor + 1;
        }

        return null;
    }

    private void CountOrSumAscii(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int search = offset + anchorPrefixLength;
        int lastAnchor = haystack.Length - (fixedAsciiLength - anchorPrefixLength);
        if (hasThreeWordWhitespaceShape &&
            firstWhitespaceCount == 1 &&
            search <= lastAnchor)
        {
            if (Avx2.IsSupported && lastAnchor - search + 1 >= Vector256<byte>.Count)
            {
                CountOrSumThreeWordWhitespaceAsciiVector256(
                    haystack,
                    search,
                    lastAnchor,
                    sumSpans,
                    ref count,
                    ref spanSum);
                return;
            }

            if (Sse2.IsSupported && lastAnchor - search + 1 >= Vector128<byte>.Count)
            {
                CountOrSumThreeWordWhitespaceAsciiVector128(
                    haystack,
                    search,
                    lastAnchor,
                    sumSpans,
                    ref count,
                    ref spanSum);
                return;
            }
        }

        while (search <= lastAnchor)
        {
            int anchor = FindNextAsciiWhitespace(haystack, search, lastAnchor);
            if (anchor < 0)
            {
                return;
            }

            int start = anchor - anchorPrefixLength;
            bool matched = hasThreeWordWhitespaceShape && firstWhitespaceCount == 1
                ? TryMatchThreeWordWhitespaceAsciiAtKnownFirstWhitespace(haystack, start)
                : TryMatchAsciiAt(haystack, start, out _);
            if (matched)
            {
                count++;
                if (sumSpans)
                {
                    spanSum += fixedAsciiLength;
                }

                offset = start + fixedAsciiLength;
                search = offset + anchorPrefixLength;
                continue;
            }

            search = anchor + 1;
        }
    }

    private void CountOrSumThreeWordWhitespaceAsciiRange(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        if (end - start < fixedAsciiLength)
        {
            return;
        }

        int search = start + anchorPrefixLength;
        int lastAnchor = end - (fixedAsciiLength - anchorPrefixLength);
        if (search > lastAnchor)
        {
            return;
        }

        if (Avx2.IsSupported && lastAnchor - search + 1 >= Vector256<byte>.Count)
        {
            CountOrSumThreeWordWhitespaceAsciiVector256(
                haystack,
                search,
                lastAnchor,
                sumSpans,
                ref count,
                ref spanSum);
            return;
        }

        if (Sse2.IsSupported && lastAnchor - search + 1 >= Vector128<byte>.Count)
        {
            CountOrSumThreeWordWhitespaceAsciiVector128(
                haystack,
                search,
                lastAnchor,
                sumSpans,
                ref count,
                ref spanSum);
            return;
        }

        int nextAllowedAnchor = search;
        CountOrSumThreeWordWhitespaceAsciiScalar(
            haystack,
            search,
            lastAnchor,
            sumSpans,
            ref count,
            ref spanSum,
            ref nextAllowedAnchor);
    }

    private int CountOrSumGenericRange(
        ReadOnlySpan<byte> haystack,
        int start,
        int end,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        int offset = start;
        int nextSegmentStart = end;
        while (offset < end && offset < haystack.Length)
        {
            if (TryMatchGenericAt(haystack, offset, out int length))
            {
                count++;
                if (sumSpans)
                {
                    spanSum += length;
                }

                offset += length;
                nextSegmentStart = Math.Max(nextSegmentStart, offset);
                continue;
            }

            offset++;
        }

        return Math.Min(nextSegmentStart, haystack.Length);
    }

    private void CountOrSumThreeWordWhitespaceAsciiVector256(
        ReadOnlySpan<byte> haystack,
        int search,
        int lastAnchor,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int nextAllowedAnchor = search;
        int offset = search;
        int vectorEnd = lastAnchor - Vector256<byte>.Count + 1;
        while (offset <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = WhitespaceMaskVector256(block);
            CountOrSumThreeWordWhitespaceAsciiMask(
                haystack,
                offset,
                mask,
                sumSpans,
                ref count,
                ref spanSum,
                ref nextAllowedAnchor);
            offset += Vector256<byte>.Count;
        }

        CountOrSumThreeWordWhitespaceAsciiScalar(
            haystack,
            offset,
            lastAnchor,
            sumSpans,
            ref count,
            ref spanSum,
            ref nextAllowedAnchor);
    }

    private void CountOrSumThreeWordWhitespaceAsciiVector128(
        ReadOnlySpan<byte> haystack,
        int search,
        int lastAnchor,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int nextAllowedAnchor = search;
        int offset = search;
        int vectorEnd = lastAnchor - Vector128<byte>.Count + 1;
        while (offset <= vectorEnd)
        {
            var block = Vector128.LoadUnsafe(ref reference, (nuint)offset);
            uint mask = WhitespaceMaskVector128(block);
            CountOrSumThreeWordWhitespaceAsciiMask(
                haystack,
                offset,
                mask,
                sumSpans,
                ref count,
                ref spanSum,
                ref nextAllowedAnchor);
            offset += Vector128<byte>.Count;
        }

        CountOrSumThreeWordWhitespaceAsciiScalar(
            haystack,
            offset,
            lastAnchor,
            sumSpans,
            ref count,
            ref spanSum,
            ref nextAllowedAnchor);
    }

    private void CountOrSumThreeWordWhitespaceAsciiMask(
        ReadOnlySpan<byte> haystack,
        int blockStart,
        uint mask,
        bool sumSpans,
        ref long count,
        ref long spanSum,
        ref int nextAllowedAnchor)
    {
        while (mask != 0)
        {
            int bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            int anchor = blockStart + bit;
            if (anchor < nextAllowedAnchor)
            {
                continue;
            }

            int start = anchor - anchorPrefixLength;
            if (!TryMatchThreeWordWhitespaceAsciiAtKnownFirstWhitespace(haystack, start))
            {
                continue;
            }

            count++;
            if (sumSpans)
            {
                spanSum += fixedAsciiLength;
            }

            nextAllowedAnchor = start + fixedAsciiLength + anchorPrefixLength;
        }
    }

    private void CountOrSumThreeWordWhitespaceAsciiScalar(
        ReadOnlySpan<byte> haystack,
        int search,
        int lastAnchor,
        bool sumSpans,
        ref long count,
        ref long spanSum,
        ref int nextAllowedAnchor)
    {
        while (search <= lastAnchor)
        {
            int anchor = FindNextAsciiWhitespace(haystack, search, lastAnchor);
            if (anchor < 0)
            {
                return;
            }

            search = anchor + 1;
            if (anchor < nextAllowedAnchor)
            {
                continue;
            }

            int start = anchor - anchorPrefixLength;
            if (!TryMatchThreeWordWhitespaceAsciiAtKnownFirstWhitespace(haystack, start))
            {
                continue;
            }

            count++;
            if (sumSpans)
            {
                spanSum += fixedAsciiLength;
            }

            nextAllowedAnchor = start + fixedAsciiLength + anchorPrefixLength;
        }
    }

    private RegexMatch? FindGeneric(ReadOnlySpan<byte> haystack, int startAt)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset < haystack.Length)
        {
            if (TryMatchGenericAt(haystack, offset, out int length))
            {
                return new RegexMatch(offset, length);
            }

            offset++;
        }

        return null;
    }

    private void CountOrSumGeneric(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset < haystack.Length)
        {
            if (TryMatchGenericAt(haystack, offset, out int length))
            {
                count++;
                if (sumSpans)
                {
                    spanSum += length;
                }

                offset += length;
                continue;
            }

            offset++;
        }
    }

    private bool TryMatchAsciiAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if (start < 0 || fixedAsciiLength > haystack.Length - start)
        {
            return false;
        }

        if (hasThreeWordWhitespaceShape)
        {
            return TryMatchThreeWordWhitespaceAsciiAt(haystack, start, out length);
        }

        int position = start;
        for (int tokenIndex = 0; tokenIndex < tokenKinds.Length; tokenIndex++)
        {
            byte tokenKind = tokenKinds[tokenIndex];
            int tokenCount = tokenCounts[tokenIndex];
            for (int count = 0; count < tokenCount; count++)
            {
                byte value = haystack[position++];
                if (tokenKind == WordKind
                    ? !IsAsciiWord(value)
                    : !IsRegexWhitespace(value))
                {
                    return false;
                }
            }
        }

        length = fixedAsciiLength;
        return true;
    }

    private bool TryMatchThreeWordWhitespaceAsciiAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        int firstWhitespaceStart = start + firstWordCount;
        int secondWordStart = firstWhitespaceStart + firstWhitespaceCount;
        int secondWhitespaceStart = secondWordStart + secondWordCount;
        int thirdWordStart = secondWhitespaceStart + secondWhitespaceCount;
        int end = thirdWordStart + thirdWordCount;
        if (end > haystack.Length ||
            !AllWhitespace(haystack, firstWhitespaceStart, firstWhitespaceCount) ||
            !AllWhitespace(haystack, secondWhitespaceStart, secondWhitespaceCount) ||
            !AllWord(haystack, start, firstWordCount) ||
            !AllWord(haystack, secondWordStart, secondWordCount) ||
            !AllWord(haystack, thirdWordStart, thirdWordCount))
        {
            return false;
        }

        length = end - start;
        return true;
    }

    private bool TryMatchThreeWordWhitespaceAsciiAtKnownFirstWhitespace(ReadOnlySpan<byte> haystack, int start)
    {
        int secondWordStart = start + firstWordCount + firstWhitespaceCount;
        int secondWhitespaceStart = secondWordStart + secondWordCount;
        int thirdWordStart = secondWhitespaceStart + secondWhitespaceCount;
        return AllWhitespace(haystack, secondWhitespaceStart, secondWhitespaceCount) &&
            AllWord(haystack, start, firstWordCount) &&
            AllWord(haystack, secondWordStart, secondWordCount) &&
            AllWord(haystack, thirdWordStart, thirdWordCount);
    }

    private bool TryMatchGenericAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length)
        {
            return false;
        }

        int position = start;
        for (int tokenIndex = 0; tokenIndex < tokenKinds.Length; tokenIndex++)
        {
            byte tokenKind = tokenKinds[tokenIndex];
            int tokenCount = tokenCounts[tokenIndex];
            RegexSyntaxKind atomKind = tokenKind == WordKind
                ? RegexSyntaxKind.WordClass
                : RegexSyntaxKind.WhitespaceClass;
            for (int count = 0; count < tokenCount; count++)
            {
                if (!TryAtomMatchLength(haystack, position, atomKind, out int atomLength))
                {
                    return false;
                }

                position += atomLength;
            }
        }

        length = position - start;
        return true;
    }

    private bool TryAtomMatchLength(ReadOnlySpan<byte> haystack, int position, RegexSyntaxKind atomKind, out int length)
    {
        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            atomKind,
            ReadOnlySpan<byte>.Empty,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out length);
    }

    private bool CanUseAsciiFastPath(ReadOnlySpan<byte> haystack)
    {
        return !options.UnicodeClasses || !ContainsNonAscii(haystack);
    }

    private static bool ContainsNonAscii(ReadOnlySpan<byte> haystack)
    {
        return haystack.IndexOfAnyExceptInRange((byte)0, (byte)0x7F) >= 0;
    }

    private static int FindNextAsciiWhitespace(ReadOnlySpan<byte> haystack, int start, int inclusiveEnd)
    {
        for (int index = start; index <= inclusiveEnd; index++)
        {
            if (IsRegexWhitespace(haystack[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryCollectTokens(
        RegexSyntaxNode root,
        out List<byte>? tokenKinds,
        out List<int>? tokenCounts)
    {
        tokenKinds = null;
        tokenCounts = null;
        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence)
        {
            return false;
        }

        tokenKinds = [];
        tokenCounts = [];
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            if (!TryGetToken(sequence.Nodes[index], out byte tokenKind, out int tokenCount))
            {
                tokenKinds = null;
                tokenCounts = null;
                return false;
            }

            AddToken(tokenKinds, tokenCounts, tokenKind, tokenCount);
        }

        return tokenKinds.Count != 0;
    }

    private static bool TryGetToken(RegexSyntaxNode node, out byte tokenKind, out int tokenCount)
    {
        tokenKind = 0;
        tokenCount = 0;
        node = UnwrapTransparentGroups(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.WordClass })
        {
            tokenKind = WordKind;
            tokenCount = 1;
            return true;
        }

        if (node is RegexAtomNode { Kind: RegexSyntaxKind.WhitespaceClass })
        {
            tokenKind = WhitespaceKind;
            tokenCount = 1;
            return true;
        }

        if (node is RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: { } maximum,
                Lazy: false,
            } repetition &&
            maximum == repetition.Minimum &&
            maximum <= MaxRepeat &&
            TryGetToken(repetition.Child, out byte repeatedKind, out int repeatedCount) &&
            repeatedCount == 1)
        {
            tokenKind = repeatedKind;
            tokenCount = maximum;
            return true;
        }

        return false;
    }

    private static void AddToken(List<byte> tokenKinds, List<int> tokenCounts, byte tokenKind, int tokenCount)
    {
        if (tokenKinds.Count > 0 && tokenKinds[^1] == tokenKind)
        {
            tokenCounts[^1] += tokenCount;
            return;
        }

        tokenKinds.Add(tokenKind);
        tokenCounts.Add(tokenCount);
    }

    private static bool IsThreeWordWhitespaceShape(List<byte> tokenKinds, List<int> tokenCounts)
    {
        return tokenKinds.Count == 5 &&
            tokenCounts.Count == 5 &&
            tokenKinds[0] == WordKind &&
            tokenKinds[1] == WhitespaceKind &&
            tokenKinds[2] == WordKind &&
            tokenKinds[3] == WhitespaceKind &&
            tokenKinds[4] == WordKind;
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
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

    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value is >= (byte)'0' and <= (byte)'9' ||
            value == (byte)'_';
    }

    private static bool IsRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static uint WhitespaceMaskVector256(Vector256<byte> block)
    {
        Vector256<byte> matches = Avx2.Or(
            Avx2.Or(
                Avx2.CompareEqual(block, SpaceVector256),
                Avx2.CompareEqual(block, TabVector256)),
            Avx2.Or(
                Avx2.CompareEqual(block, LineFeedVector256),
                Avx2.CompareEqual(block, VerticalTabVector256)));
        matches = Avx2.Or(
            matches,
            Avx2.Or(
                Avx2.CompareEqual(block, FormFeedVector256),
                Avx2.CompareEqual(block, CarriageReturnVector256)));
        return matches.ExtractMostSignificantBits();
    }

    private static uint WhitespaceMaskVector128(Vector128<byte> block)
    {
        Vector128<byte> matches = Sse2.Or(
            Sse2.Or(
                Sse2.CompareEqual(block, SpaceVector128),
                Sse2.CompareEqual(block, TabVector128)),
            Sse2.Or(
                Sse2.CompareEqual(block, LineFeedVector128),
                Sse2.CompareEqual(block, VerticalTabVector128)));
        matches = Sse2.Or(
            matches,
            Sse2.Or(
                Sse2.CompareEqual(block, FormFeedVector128),
                Sse2.CompareEqual(block, CarriageReturnVector128)));
        return matches.ExtractMostSignificantBits();
    }

    private static bool AllWord(ReadOnlySpan<byte> haystack, int start, int length)
    {
        int end = start + length;
        for (int index = start; index < end; index++)
        {
            if (!IsAsciiWord(haystack[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllWhitespace(ReadOnlySpan<byte> haystack, int start, int length)
    {
        int end = start + length;
        for (int index = start; index < end; index++)
        {
            if (!IsRegexWhitespace(haystack[index]))
            {
                return false;
            }
        }

        return true;
    }
}
