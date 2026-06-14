using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Scout;

internal sealed class RegexSimpleSequenceEngine
{
    private const int MinimumLiteralRunLength = 3;
    private const int MaxSegments = 512;
    private const int MaxBoundedRepeat = 1024;
    private const int MaxSelectiveStartBytes = 64;

    private readonly RegexSimpleSequenceSegment[] segments;
    private readonly RegexSimpleSequenceSegment[]? repeatedSegments;
    private readonly int repeatedMinimum;
    private readonly int repeatedMaximum;
    private readonly bool repeatedLazy;
    private readonly bool hasDisjointRunSuffixBoundary;
    private readonly RegexSimpleSequenceSegment boundaryRunSegment;
    private readonly RegexSimpleSequenceSegment boundarySuffixSegment;
    private readonly int boundaryRunMinimum;
    private readonly RegexCompileOptions boundaryOptions;
    private readonly bool boundaryStartRequiresUtf8Boundary;
    private readonly bool hasFixedWidthLiteralSuffix;
    private readonly int fixedWidthLength;
    private readonly byte fixedWidthSuffixLiteral;
    private readonly bool hasLiteralWhitespaceLiteral;
    private readonly byte[] literalWhitespacePrefix;
    private readonly byte[] literalWhitespaceSuffix;
    private readonly int literalWhitespaceMinimum;
    private readonly int? literalWhitespaceMaximum;
    private readonly bool hasAsciiLetterRunLiteralSuffix;
    private readonly int asciiLetterRunMinimum;
    private readonly byte[] asciiLetterRunSuffix;

    private RegexSimpleSequenceEngine(List<RegexSimpleSequenceSegment> segments)
    {
        this.segments = [.. segments];
        repeatedSegments = null;
        repeatedMinimum = 0;
        repeatedMaximum = 0;
        repeatedLazy = false;
        hasDisjointRunSuffixBoundary = false;
        boundaryRunSegment = default;
        boundarySuffixSegment = default;
        boundaryRunMinimum = 0;
        boundaryOptions = default;
        boundaryStartRequiresUtf8Boundary = false;
        hasFixedWidthLiteralSuffix = TryGetFixedWidthLiteralSuffix(
            this.segments,
            out fixedWidthLength,
            out fixedWidthSuffixLiteral);
        hasLiteralWhitespaceLiteral = TryGetLiteralWhitespaceLiteral(
            this.segments,
            out literalWhitespacePrefix,
            out literalWhitespaceMinimum,
            out literalWhitespaceMaximum,
            out literalWhitespaceSuffix);
        hasAsciiLetterRunLiteralSuffix = TryGetAsciiLetterRunLiteralSuffix(
            this.segments,
            out asciiLetterRunMinimum,
            out asciiLetterRunSuffix);
    }

    private RegexSimpleSequenceEngine(List<RegexSimpleSequenceSegment> repeatedSegments, int repeatedMinimum, int repeatedMaximum, bool repeatedLazy)
    {
        segments = [];
        this.repeatedSegments = [.. repeatedSegments];
        this.repeatedMinimum = repeatedMinimum;
        this.repeatedMaximum = repeatedMaximum;
        this.repeatedLazy = repeatedLazy;
        hasDisjointRunSuffixBoundary = false;
        boundaryRunSegment = default;
        boundarySuffixSegment = default;
        boundaryRunMinimum = 0;
        boundaryOptions = default;
        boundaryStartRequiresUtf8Boundary = false;
        hasFixedWidthLiteralSuffix = false;
        fixedWidthLength = 0;
        fixedWidthSuffixLiteral = 0;
        hasLiteralWhitespaceLiteral = false;
        literalWhitespacePrefix = [];
        literalWhitespaceSuffix = [];
        literalWhitespaceMinimum = 0;
        literalWhitespaceMaximum = null;
        hasAsciiLetterRunLiteralSuffix = false;
        asciiLetterRunMinimum = 0;
        asciiLetterRunSuffix = [];
    }

    private RegexSimpleSequenceEngine(
        RegexSimpleSequenceSegment runSegment,
        RegexSimpleSequenceSegment suffixSegment,
        int runMinimum,
        RegexCompileOptions wordBoundaryOptions,
        bool startRequiresUtf8Boundary)
    {
        segments = [];
        repeatedSegments = null;
        repeatedMinimum = 0;
        repeatedMaximum = 0;
        repeatedLazy = false;
        hasDisjointRunSuffixBoundary = true;
        boundaryRunSegment = runSegment;
        boundarySuffixSegment = suffixSegment;
        boundaryRunMinimum = runMinimum;
        boundaryOptions = wordBoundaryOptions;
        boundaryStartRequiresUtf8Boundary = startRequiresUtf8Boundary;
        hasFixedWidthLiteralSuffix = false;
        fixedWidthLength = 0;
        fixedWidthSuffixLiteral = 0;
        hasLiteralWhitespaceLiteral = false;
        literalWhitespacePrefix = [];
        literalWhitespaceSuffix = [];
        literalWhitespaceMinimum = 0;
        literalWhitespaceMaximum = null;
        hasAsciiLetterRunLiteralSuffix = false;
        asciiLetterRunMinimum = 0;
        asciiLetterRunSuffix = [];
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexSimpleSequenceEngine? engine)
    {
        engine = null;
        if (TryCreateDisjointRunSuffixBoundary(root, options, out engine))
        {
            return true;
        }

        if (options.Utf8 || options.UnicodeClasses)
        {
            return false;
        }

        if (TryCreateRepeatedRoot(root, options, out engine))
        {
            return true;
        }

        var segments = new List<RegexSimpleSequenceSegment>();
        bool sawVariableRepetition = false;
        if (!TryAppend(root, options, segments, ref sawVariableRepetition) ||
            !sawVariableRepetition &&
            !TryGetFixedWidthLiteralSuffix(segments, out _, out _) ||
            segments.Count == 0 ||
            segments.Count > MaxSegments ||
            LongestLiteralRun(segments) < MinimumLiteralRunLength &&
            !HasSelectiveRequiredStart(segments))
        {
            return false;
        }

        engine = new RegexSimpleSequenceEngine(segments);
        return true;
    }

    public static bool TryCreateSingleRepeatedByteAtom(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexSimpleSequenceEngine? engine)
    {
        engine = null;
        if (options.Utf8 || options.UnicodeClasses)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexRepetitionNode
            {
                Minimum: > 0,
                Maximum: { } maximum,
            } repetition ||
            maximum > MaxBoundedRepeat)
        {
            return false;
        }

        var segments = new List<RegexSimpleSequenceSegment>(capacity: 1);
        bool sawVariableRepetition = false;
        if (!TryAppendRepetition(repetition, options, segments, ref sawVariableRepetition) ||
            segments.Count != 1 ||
            !HasSelectiveRequiredStart(segments))
        {
            return false;
        }

        engine = new RegexSimpleSequenceEngine(segments);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (hasDisjointRunSuffixBoundary)
        {
            return FindDisjointRunSuffixBoundary(haystack, startOffset);
        }

        if (repeatedSegments is null && segments.Length == 1)
        {
            return FindSingleSegment(segments[0], haystack, startOffset);
        }

        if (hasFixedWidthLiteralSuffix)
        {
            return FindFixedWidthLiteralSuffix(haystack, startOffset);
        }

        if (hasLiteralWhitespaceLiteral)
        {
            return FindLiteralWhitespaceLiteral(haystack, startOffset);
        }

        if (hasAsciiLetterRunLiteralSuffix)
        {
            return FindAsciiLetterRunLiteralSuffix(haystack, startOffset);
        }

        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (CanStartAt(haystack, start) &&
                TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    public bool TryCountNonOverlapping(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        out long count,
        out long spanSum)
    {
        count = 0;
        spanSum = 0;
        if (hasDisjointRunSuffixBoundary)
        {
            int offset = Math.Clamp(startAt, 0, haystack.Length);
            while (FindDisjointRunSuffixBoundary(haystack, offset) is RegexMatch match)
            {
                count++;
                if (sumSpans)
                {
                    spanSum += match.Length;
                }

                offset = match.End;
            }

            return true;
        }

        if (TryCountFixedWidthLiteralSuffix(haystack, startAt, sumSpans, ref count, ref spanSum))
        {
            return true;
        }

        if (TryCountLiteralWhitespaceLiteral(haystack, startAt, sumSpans, ref count, ref spanSum))
        {
            return true;
        }

        if (TryCountAsciiLetterRunLiteralSuffix(haystack, startAt, sumSpans, ref count, ref spanSum))
        {
            return true;
        }

        if (TryCountRepeatedCapitalizedWords(haystack, startAt, sumSpans, ref count, ref spanSum))
        {
            return true;
        }

        if (!TryGetSingleRepeatedAtom(out RegexSimpleSequenceSegment segment, out int minimum, out int? maximum, out bool lazy))
        {
            return false;
        }

        int position = Math.Clamp(startAt, 0, haystack.Length);
        if (segment.MatcherKind == RegexSimpleSequenceByteMatcherKind.AsciiLetter)
        {
            if (sumSpans)
            {
                CountAsciiLetterRuns(minimum, maximum, lazy, haystack, position, sumSpans, ref count, ref spanSum);
            }
            else
            {
                CountAsciiLetterRunsCountOnly(minimum, maximum, lazy, haystack, position, ref count);
            }

            return true;
        }

        if (segment.MatcherKind == RegexSimpleSequenceByteMatcherKind.AsciiDigit)
        {
            if (sumSpans)
            {
                CountAsciiDigitRuns(minimum, maximum, lazy, haystack, position, sumSpans, ref count, ref spanSum);
            }
            else
            {
                CountAsciiDigitRunsCountOnly(minimum, maximum, lazy, haystack, position, ref count);
            }

            return true;
        }

        if (segment.MatcherKind == RegexSimpleSequenceByteMatcherKind.AsciiWord)
        {
            if (sumSpans)
            {
                CountAsciiWordRuns(minimum, maximum, lazy, haystack, position, sumSpans, ref count, ref spanSum);
            }
            else
            {
                CountAsciiWordRunsCountOnly(minimum, maximum, lazy, haystack, position, ref count);
            }

            return true;
        }

        int runLength = 0;
        while (position < haystack.Length)
        {
            if (segment.AtomMatches(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
        return true;
    }

    private RegexMatch? FindAsciiLetterRunLiteralSuffix(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryFindAsciiLetterRunLiteralSuffix(haystack, Math.Clamp(startAt, 0, haystack.Length), out int start, out int end)
            ? new RegexMatch(start, end - start)
            : null;
    }

    private bool TryCountAsciiLetterRunLiteralSuffix(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        if (!hasAsciiLetterRunLiteralSuffix)
        {
            return false;
        }

        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindAsciiLetterRunLiteralSuffix(haystack, offset, out int start, out int end))
        {
            count++;
            if (sumSpans)
            {
                spanSum += end - start;
            }

            offset = end;
        }

        return true;
    }

    private bool TryFindAsciiLetterRunLiteralSuffix(
        ReadOnlySpan<byte> haystack,
        int offset,
        out int start,
        out int end)
    {
        ReadOnlySpan<byte> suffix = asciiLetterRunSuffix;
        int search = Math.Min(haystack.Length, offset + asciiLetterRunMinimum);
        while (search <= haystack.Length - suffix.Length)
        {
            int relative = haystack[search..].IndexOf(suffix);
            if (relative < 0)
            {
                break;
            }

            int suffixAt = search + relative;
            int runStart = suffixAt - 1;
            while (runStart >= offset && RegexSimpleSequenceSegment.IsAsciiLetter(haystack[runStart]))
            {
                runStart--;
            }

            runStart++;
            if (suffixAt - runStart < asciiLetterRunMinimum)
            {
                search = suffixAt + 1;
                continue;
            }

            int runEnd = suffixAt + suffix.Length;
            while (runEnd < haystack.Length && RegexSimpleSequenceSegment.IsAsciiLetter(haystack[runEnd]))
            {
                runEnd++;
            }

            int firstSuffixStart = runStart + asciiLetterRunMinimum;
            int lastRelative = haystack[firstSuffixStart..runEnd].LastIndexOf(suffix);
            start = runStart;
            end = firstSuffixStart + lastRelative + suffix.Length;
            return true;
        }

        start = 0;
        end = 0;
        return false;
    }

    private bool TryCountRepeatedCapitalizedWords(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        if (!IsRepeatedCapitalizedWordPattern(out int wordMinimum, out int wordMaximum, out bool lazy))
        {
            return false;
        }

        int position = Math.Clamp(startAt, 0, haystack.Length);
        int wordsPerMatch = lazy ? wordMinimum : wordMaximum;
        while (position < haystack.Length)
        {
            int runStart = FindNextCapitalizedWordStart(haystack, position);
            if (runStart < 0)
            {
                return true;
            }

            int wordCount = 0;
            int runPosition = runStart;
            while (wordCount < wordsPerMatch &&
                TryConsumeCapitalizedWord(haystack, runPosition, out int nextPosition))
            {
                wordCount++;
                runPosition = nextPosition;
            }

            if (wordCount >= wordMinimum)
            {
                count++;
                if (sumSpans)
                {
                    spanSum += runPosition - runStart;
                }

                position = runPosition;
                continue;
            }

            position = Math.Max(runPosition, runStart + 1);
        }

        return true;
    }

    private bool IsRepeatedCapitalizedWordPattern(out int minimum, out int maximum, out bool lazy)
    {
        minimum = 0;
        maximum = 0;
        lazy = false;
        if (repeatedSegments is not { Length: 3 } ||
            repeatedMinimum <= 0 ||
            repeatedMaximum < repeatedMinimum ||
            repeatedSegments[0] is not { MatcherKind: RegexSimpleSequenceByteMatcherKind.AsciiUppercase, Minimum: 1, Maximum: 1, Lazy: false } ||
            repeatedSegments[1] is not { MatcherKind: RegexSimpleSequenceByteMatcherKind.AsciiLowercase, Minimum: 1, Maximum: null, Lazy: false } ||
            repeatedSegments[2] is not { MatcherKind: RegexSimpleSequenceByteMatcherKind.RegexWhitespace, Minimum: 0, Maximum: null, Lazy: false })
        {
            return false;
        }

        minimum = repeatedMinimum;
        maximum = repeatedMaximum;
        lazy = repeatedLazy;
        return true;
    }

    private static int FindNextCapitalizedWordStart(ReadOnlySpan<byte> haystack, int position)
    {
        while (position + 1 < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiUppercase(haystack[position]) &&
                RegexSimpleSequenceSegment.IsAsciiLowercase(haystack[position + 1]))
            {
                return position;
            }

            position++;
        }

        return -1;
    }

    private static bool TryConsumeCapitalizedWord(ReadOnlySpan<byte> haystack, int position, out int nextPosition)
    {
        nextPosition = position;
        if (position + 1 >= haystack.Length ||
            !RegexSimpleSequenceSegment.IsAsciiUppercase(haystack[position]) ||
            !RegexSimpleSequenceSegment.IsAsciiLowercase(haystack[position + 1]))
        {
            return false;
        }

        position += 2;
        while (position < haystack.Length &&
            RegexSimpleSequenceSegment.IsAsciiLowercase(haystack[position]))
        {
            position++;
        }

        while (position < haystack.Length &&
            RegexSimpleSequenceSegment.IsRegexWhitespace(haystack[position]))
        {
            position++;
        }

        nextPosition = position;
        return true;
    }

    private bool TryGetSingleRepeatedAtom(
        out RegexSimpleSequenceSegment segment,
        out int minimum,
        out int? maximum,
        out bool lazy)
    {
        if (repeatedSegments is null)
        {
            if (segments.Length == 1)
            {
                segment = segments[0];
                minimum = segment.Minimum;
                maximum = segment.Maximum;
                lazy = segment.Lazy;
                return true;
            }
        }
        else if (repeatedSegments.Length == 1 &&
            repeatedSegments[0].Minimum == 1 &&
            repeatedSegments[0].Maximum == 1)
        {
            segment = repeatedSegments[0];
            minimum = repeatedMinimum;
            maximum = repeatedMaximum;
            lazy = repeatedLazy;
            return true;
        }

        segment = default;
        minimum = 0;
        maximum = null;
        lazy = false;
        return false;
    }

    private static void CountAsciiLetterRuns(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        int runLength = 0;
        while (position < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiLetter(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
    }

    private static void CountAsciiLetterRunsCountOnly(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count)
    {
        if (Avx2.IsSupported && haystack.Length - position >= Vector256<byte>.Count)
        {
            CountAsciiLetterRunsCountOnlyAvx2(minimum, maximum, lazy, haystack, position, ref count);
            return;
        }

        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int length = haystack.Length;
        while (position < length)
        {
            while (position < length && !RegexSimpleSequenceSegment.IsAsciiLetter(Unsafe.Add(ref reference, position)))
            {
                position++;
            }

            if (position >= length)
            {
                break;
            }

            int runStart = position;
            while (position < length && RegexSimpleSequenceSegment.IsAsciiLetter(Unsafe.Add(ref reference, position)))
            {
                position++;
            }

            AddRunCountOnly(minimum, maximum, lazy, position - runStart, ref count);
        }
    }

    private static void CountAsciiLetterRunsCountOnlyAvx2(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int length = haystack.Length;
        int vectorEnd = length - Vector256<byte>.Count;
        int runLength = 0;
        var caseFold = Vector256.Create((byte)0x20);
        var beforeA = Vector256.Create((sbyte)('a' - 1));
        var afterZ = Vector256.Create((sbyte)('z' + 1));
        while (position <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)position);
            Vector256<sbyte> folded = Avx2.Or(block, caseFold).AsSByte();
            Vector256<byte> matches = Avx2.And(
                Avx2.CompareGreaterThan(folded, beforeA),
                Avx2.CompareGreaterThan(afterZ, folded)).AsByte();
            uint mask = matches.ExtractMostSignificantBits();
            AccumulateRunMask(minimum, maximum, lazy, mask, Vector256<byte>.Count, ref runLength, ref count);
            position += Vector256<byte>.Count;
        }

        while (position < length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiLetter(Unsafe.Add(ref reference, position)))
            {
                runLength++;
            }
            else
            {
                AddRunCountOnly(minimum, maximum, lazy, runLength, ref count);
                runLength = 0;
            }

            position++;
        }

        AddRunCountOnly(minimum, maximum, lazy, runLength, ref count);
    }

    private static void AccumulateRunMask(
        int minimum,
        int? maximum,
        bool lazy,
        uint mask,
        int width,
        ref int runLength,
        ref long count)
    {
        uint fullMask = width == 32 ? uint.MaxValue : (1u << width) - 1u;
        mask &= fullMask;
        if (mask == fullMask)
        {
            runLength += width;
            return;
        }

        if (mask == 0)
        {
            AddRunCountOnly(minimum, maximum, lazy, runLength, ref count);
            runLength = 0;
            return;
        }

        int consumed = 0;
        while (mask != 0)
        {
            int zeros = BitOperations.TrailingZeroCount(mask);
            if (zeros > 0)
            {
                AddRunCountOnly(minimum, maximum, lazy, runLength, ref count);
                runLength = 0;
                consumed += zeros;
                mask >>= zeros;
            }

            int ones = BitOperations.TrailingZeroCount(~mask);
            runLength += ones;
            consumed += ones;
            mask >>= ones;
        }

        if (consumed < width)
        {
            AddRunCountOnly(minimum, maximum, lazy, runLength, ref count);
            runLength = 0;
        }
    }

    private static void CountAsciiDigitRuns(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        int runLength = 0;
        while (position < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiDigit(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
    }

    private static void CountAsciiDigitRunsCountOnly(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int length = haystack.Length;
        while (position < length)
        {
            while (position < length && !RegexSimpleSequenceSegment.IsAsciiDigit(Unsafe.Add(ref reference, position)))
            {
                position++;
            }

            if (position >= length)
            {
                break;
            }

            int runStart = position;
            while (position < length && RegexSimpleSequenceSegment.IsAsciiDigit(Unsafe.Add(ref reference, position)))
            {
                position++;
            }

            AddRunCountOnly(minimum, maximum, lazy, position - runStart, ref count);
        }
    }

    private static void CountAsciiWordRuns(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        int runLength = 0;
        while (position < haystack.Length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiWord(haystack[position]))
            {
                runLength++;
            }
            else
            {
                AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
                runLength = 0;
            }

            position++;
        }

        AddRun(minimum, maximum, lazy, runLength, sumSpans, ref count, ref spanSum);
    }

    private static void CountAsciiWordRunsCountOnly(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count)
    {
        if (Avx2.IsSupported && haystack.Length - position >= Vector256<byte>.Count)
        {
            CountAsciiWordRunsCountOnlyAvx2(minimum, maximum, lazy, haystack, position, ref count);
            return;
        }

        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int length = haystack.Length;
        while (position < length)
        {
            while (position < length && !RegexSimpleSequenceSegment.IsAsciiWord(Unsafe.Add(ref reference, position)))
            {
                position++;
            }

            if (position >= length)
            {
                break;
            }

            int runStart = position;
            while (position < length && RegexSimpleSequenceSegment.IsAsciiWord(Unsafe.Add(ref reference, position)))
            {
                position++;
            }

            AddRunCountOnly(minimum, maximum, lazy, position - runStart, ref count);
        }
    }

    private static void CountAsciiWordRunsCountOnlyAvx2(
        int minimum,
        int? maximum,
        bool lazy,
        ReadOnlySpan<byte> haystack,
        int position,
        ref long count)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int length = haystack.Length;
        int vectorEnd = length - Vector256<byte>.Count;
        int runLength = 0;
        var caseFold = Vector256.Create((byte)0x20);
        var beforeA = Vector256.Create((sbyte)('a' - 1));
        var afterZ = Vector256.Create((sbyte)('z' + 1));
        var beforeZero = Vector256.Create((sbyte)('0' - 1));
        var afterNine = Vector256.Create((sbyte)('9' + 1));
        var underscore = Vector256.Create((byte)'_');
        while (position <= vectorEnd)
        {
            var block = Vector256.LoadUnsafe(ref reference, (nuint)position);
            Vector256<sbyte> signed = block.AsSByte();
            Vector256<sbyte> folded = Avx2.Or(block, caseFold).AsSByte();
            Vector256<byte> letterMatches = Avx2.And(
                Avx2.CompareGreaterThan(folded, beforeA),
                Avx2.CompareGreaterThan(afterZ, folded)).AsByte();
            Vector256<byte> digitMatches = Avx2.And(
                Avx2.CompareGreaterThan(signed, beforeZero),
                Avx2.CompareGreaterThan(afterNine, signed)).AsByte();
            Vector256<byte> matches = Avx2.Or(
                Avx2.Or(letterMatches, digitMatches),
                Avx2.CompareEqual(block, underscore));
            uint mask = matches.ExtractMostSignificantBits();
            AccumulateRunMask(minimum, maximum, lazy, mask, Vector256<byte>.Count, ref runLength, ref count);
            position += Vector256<byte>.Count;
        }

        while (position < length)
        {
            if (RegexSimpleSequenceSegment.IsAsciiWord(Unsafe.Add(ref reference, position)))
            {
                runLength++;
            }
            else
            {
                AddRunCountOnly(minimum, maximum, lazy, runLength, ref count);
                runLength = 0;
            }

            position++;
        }

        AddRunCountOnly(minimum, maximum, lazy, runLength, ref count);
    }

    private static RegexMatch? FindSingleSegment(RegexSimpleSequenceSegment segment, ReadOnlySpan<byte> haystack, int startOffset)
    {
        int scan = startOffset;
        while (scan < haystack.Length)
        {
            while (scan < haystack.Length && !segment.AtomMatches(haystack[scan]))
            {
                scan++;
            }

            if (scan >= haystack.Length)
            {
                return null;
            }

            int runStart = scan;
            int maxCount = Math.Min(segment.Maximum ?? haystack.Length - runStart, haystack.Length - runStart);
            if (maxCount <= 0)
            {
                scan++;
                continue;
            }

            int matched = 0;
            while (matched < maxCount && segment.AtomMatches(haystack[runStart + matched]))
            {
                matched++;
            }

            if (matched >= segment.Minimum)
            {
                int length = segment.Lazy
                    ? segment.Minimum
                    : matched;
                return new RegexMatch(runStart, length);
            }

            scan = runStart + matched;
        }

        return null;
    }

    private RegexMatch? FindFixedWidthLiteralSuffix(ReadOnlySpan<byte> haystack, int startAt)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suffixOffset = fixedWidthLength - 1;
        int search = Math.Min(haystack.Length, offset + suffixOffset);
        while (search < haystack.Length)
        {
            int relative = haystack[search..].IndexOf(fixedWidthSuffixLiteral);
            if (relative < 0)
            {
                return null;
            }

            int suffixAt = search + relative;
            int start = suffixAt - suffixOffset;
            if (start >= offset && TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }

            search = suffixAt + 1;
        }

        return null;
    }

    private bool TryCountFixedWidthLiteralSuffix(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        if (!hasFixedWidthLiteralSuffix)
        {
            return false;
        }

        int offset = Math.Clamp(startAt, 0, haystack.Length);
        int suffixOffset = fixedWidthLength - 1;
        int search = Math.Min(haystack.Length, offset + suffixOffset);
        while (search < haystack.Length)
        {
            int relative = haystack[search..].IndexOf(fixedWidthSuffixLiteral);
            if (relative < 0)
            {
                return true;
            }

            int suffixAt = search + relative;
            int start = suffixAt - suffixOffset;
            if (start >= offset && TryMatchAt(haystack, start, out int length))
            {
                count++;
                if (sumSpans)
                {
                    spanSum += length;
                }

                offset = start + length;
                search = Math.Min(haystack.Length, offset + suffixOffset);
                continue;
            }

            search = suffixAt + 1;
        }

        return true;
    }

    private RegexMatch? FindLiteralWhitespaceLiteral(ReadOnlySpan<byte> haystack, int startAt)
    {
        return TryFindLiteralWhitespaceLiteral(haystack, Math.Clamp(startAt, 0, haystack.Length), out int start, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    private bool TryCountLiteralWhitespaceLiteral(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        if (!hasLiteralWhitespaceLiteral)
        {
            return false;
        }

        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (TryFindLiteralWhitespaceLiteral(haystack, offset, out int start, out int length))
        {
            count++;
            if (sumSpans)
            {
                spanSum += length;
            }

            offset = start + length;
        }

        return true;
    }

    private bool TryFindLiteralWhitespaceLiteral(
        ReadOnlySpan<byte> haystack,
        int startAt,
        out int start,
        out int length)
    {
        ReadOnlySpan<byte> prefix = literalWhitespacePrefix;
        int minimumLength = prefix.Length + literalWhitespaceMinimum + literalWhitespaceSuffix.Length;
        int search = Math.Clamp(startAt, 0, haystack.Length);
        int lastStart = haystack.Length - minimumLength;
        while (search <= lastStart)
        {
            int relative = haystack[search..].IndexOf(prefix);
            if (relative < 0)
            {
                break;
            }

            int candidate = search + relative;
            if (TryMatchLiteralWhitespaceLiteralAt(haystack, candidate, out length))
            {
                start = candidate;
                return true;
            }

            search = candidate + 1;
        }

        start = 0;
        length = 0;
        return false;
    }

    private bool TryMatchLiteralWhitespaceLiteralAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        ReadOnlySpan<byte> prefix = literalWhitespacePrefix;
        if (!hasLiteralWhitespaceLiteral ||
            start < 0 ||
            start > haystack.Length - prefix.Length ||
            !haystack.Slice(start, prefix.Length).SequenceEqual(prefix))
        {
            return false;
        }

        int gapStart = start + prefix.Length;
        int available = haystack.Length - gapStart;
        int maxGap = literalWhitespaceMaximum.HasValue
            ? Math.Min(literalWhitespaceMaximum.Value, available)
            : available;
        int gapLength = 0;
        while (gapLength < maxGap &&
            RegexSimpleSequenceSegment.IsRegexWhitespace(haystack[gapStart + gapLength]))
        {
            gapLength++;
        }

        if (gapLength < literalWhitespaceMinimum)
        {
            return false;
        }

        ReadOnlySpan<byte> suffix = literalWhitespaceSuffix;
        int suffixAt = gapStart + gapLength;
        if (suffixAt > haystack.Length - suffix.Length ||
            !haystack.Slice(suffixAt, suffix.Length).SequenceEqual(suffix))
        {
            return false;
        }

        length = prefix.Length + gapLength + suffix.Length;
        return true;
    }

    private static void AddRun(
        int minimum,
        int? maximum,
        bool lazy,
        int runLength,
        bool sumSpans,
        ref long count,
        ref long spanSum)
    {
        if (runLength < minimum)
        {
            return;
        }

        if (!lazy && !maximum.HasValue)
        {
            count++;
            if (sumSpans)
            {
                spanSum += runLength;
            }

            return;
        }

        int matchLength = lazy ? minimum : maximum ?? minimum;
        int fullMatches = runLength / matchLength;
        int remainder = runLength - fullMatches * matchLength;
        count += fullMatches;
        if (sumSpans)
        {
            spanSum += (long)fullMatches * matchLength;
        }

        if (remainder >= minimum)
        {
            count++;
            if (sumSpans)
            {
                spanSum += remainder;
            }
        }
    }

    private static void AddRunCountOnly(
        int minimum,
        int? maximum,
        bool lazy,
        int runLength,
        ref long count)
    {
        if (runLength < minimum)
        {
            return;
        }

        if (!lazy && !maximum.HasValue)
        {
            count++;
            return;
        }

        int matchLength = lazy ? minimum : maximum ?? minimum;
        int fullMatches = runLength / matchLength;
        int remainder = runLength - fullMatches * matchLength;
        count += fullMatches;
        if (remainder >= minimum)
        {
            count++;
        }
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        if (start < 0 || start > haystack.Length)
        {
            length = 0;
            return false;
        }

        if (hasDisjointRunSuffixBoundary)
        {
            return TryMatchDisjointRunSuffixBoundaryAt(haystack, start, out length);
        }

        if (!CanStartAt(haystack, start))
        {
            length = 0;
            return false;
        }

        if (hasLiteralWhitespaceLiteral)
        {
            return TryMatchLiteralWhitespaceLiteralAt(haystack, start, out length);
        }

        if (repeatedSegments is not null)
        {
            return TryMatchRepeatedAt(haystack, start, out length);
        }

        if (segments.Length == 1)
        {
            return TryMatchSingleSegment(segments[0], haystack, start, out length);
        }

        var cache = new Dictionary<long, int>();
        if (TryMatchFrom(segments, 0, start, haystack, cache, out int end))
        {
            length = end - start;
            return true;
        }

        length = 0;
        return false;
    }

    private bool CanStartAt(ReadOnlySpan<byte> haystack, int start)
    {
        RegexSimpleSequenceSegment[] activeSegments = repeatedSegments ?? segments;
        return activeSegments.Length == 0 ||
            activeSegments[0].Minimum == 0 ||
            start < haystack.Length && activeSegments[0].AtomMatches(haystack[start]);
    }

    private bool TryMatchRepeatedAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int position = start;
        int count = 0;
        var ends = new List<int>();
        var cache = new Dictionary<long, int>();
        while (count < repeatedMaximum &&
            TryMatchChildOnce(haystack, position, cache, out int next) &&
            next > position)
        {
            count++;
            position = next;
            ends.Add(position);
        }

        if (count < repeatedMinimum)
        {
            length = 0;
            return false;
        }

        int selectedEnd = repeatedLazy
            ? ends[repeatedMinimum - 1]
            : ends[^1];
        length = selectedEnd - start;
        return true;
    }

    private bool TryMatchChildOnce(ReadOnlySpan<byte> haystack, int start, Dictionary<long, int> cache, out int end)
    {
        cache.Clear();
        return TryMatchFrom(repeatedSegments!, 0, start, haystack, cache, out end);
    }

    private static bool TryMatchSingleSegment(RegexSimpleSequenceSegment segment, ReadOnlySpan<byte> haystack, int start, out int length)
    {
        int maxCount = segment.Maximum ?? haystack.Length - start;
        maxCount = Math.Min(maxCount, haystack.Length - start);
        int matched = 0;
        while (matched < maxCount &&
            start + matched < haystack.Length &&
            segment.AtomMatches(haystack[start + matched]))
        {
            matched++;
        }

        if (matched < segment.Minimum)
        {
            length = 0;
            return false;
        }

        length = segment.Lazy ? segment.Minimum : matched;
        return true;
    }

    private static bool TryMatchFrom(
        RegexSimpleSequenceSegment[] activeSegments,
        int segmentIndex,
        int position,
        ReadOnlySpan<byte> haystack,
        Dictionary<long, int> cache,
        out int end)
    {
        if (segmentIndex == activeSegments.Length)
        {
            end = position;
            return true;
        }

        long key = ((long)segmentIndex << 32) | (uint)position;
        if (cache.TryGetValue(key, out int cached))
        {
            end = cached;
            return cached >= 0;
        }

        RegexSimpleSequenceSegment segment = activeSegments[segmentIndex];
        int maxCount = segment.Maximum ?? haystack.Length - position;
        maxCount = Math.Min(maxCount, haystack.Length - position);

        int matched = 0;
        int scan = position;
        while (matched < maxCount &&
            scan < haystack.Length &&
            segment.AtomMatches(haystack[scan]))
        {
            matched++;
            scan++;
        }

        if (matched >= segment.Minimum)
        {
            if (segment.Lazy)
            {
                for (int count = segment.Minimum; count <= matched; count++)
                {
                    if (TryMatchFrom(activeSegments, segmentIndex + 1, position + count, haystack, cache, out end))
                    {
                        cache[key] = end;
                        return true;
                    }
                }
            }
            else
            {
                for (int count = matched; count >= segment.Minimum; count--)
                {
                    if (TryMatchFrom(activeSegments, segmentIndex + 1, position + count, haystack, cache, out end))
                    {
                        cache[key] = end;
                        return true;
                    }
                }
            }
        }

        end = -1;
        cache[key] = -1;
        return false;
    }

    private static bool TryAppend(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                return true;
            case RegexSyntaxKind.Literal:
                return TryAppendLiteral(((RegexAtomNode)node).Value.Span, options, minimum: 1, maximum: 1, lazy: false, segments);
            case RegexSyntaxKind.Dot:
            case RegexSyntaxKind.AnyClass:
            case RegexSyntaxKind.CharacterClass:
            case RegexSyntaxKind.DigitClass:
            case RegexSyntaxKind.NotDigitClass:
            case RegexSyntaxKind.WordClass:
            case RegexSyntaxKind.NotWordClass:
            case RegexSyntaxKind.WhitespaceClass:
            case RegexSyntaxKind.NotWhitespaceClass:
                return TryAppendAtom((RegexAtomNode)node, options, minimum: 1, maximum: 1, lazy: false, segments, ref sawVariableRepetition);
            case RegexSyntaxKind.Sequence:
                return TryAppendSequence((RegexSequenceNode)node, options, segments, ref sawVariableRepetition);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAppend(group.Child, options.Apply(group.EnabledFlags, group.DisabledFlags), segments, ref sawVariableRepetition);
            case RegexSyntaxKind.Repetition:
                return TryAppendRepetition((RegexRepetitionNode)node, options, segments, ref sawVariableRepetition);
            default:
                return false;
        }
    }

    private static bool TryAppendSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                if (currentOptions.Utf8 || currentOptions.UnicodeClasses)
                {
                    return false;
                }

                continue;
            }

            if (!TryAppend(child, currentOptions, segments, ref sawVariableRepetition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendRepetition(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        if (node.Maximum.HasValue && node.Maximum.Value > MaxBoundedRepeat)
        {
            return false;
        }

        sawVariableRepetition |= node.Maximum != node.Minimum;
        RegexSyntaxNode child = UnwrapTransparentGroups(node.Child);
        if (child.Kind == RegexSyntaxKind.Literal)
        {
            ReadOnlySpan<byte> literal = ((RegexAtomNode)child).Value.Span;
            return literal.Length == 1 &&
                TryAppendLiteral(literal, options, node.Minimum, node.Maximum, node.Lazy, segments);
        }

        if (child is not RegexAtomNode atom || !CanRepeatAtom(atom, node.Maximum, options))
        {
            return false;
        }

        return TryAppendAtom(atom, options, node.Minimum, node.Maximum, node.Lazy, segments, ref sawVariableRepetition);
    }

    private RegexMatch? FindDisjointRunSuffixBoundary(ReadOnlySpan<byte> haystack, int startOffset)
    {
        int position = startOffset;
        while (position < haystack.Length)
        {
            while (position < haystack.Length && !boundaryRunSegment.AtomMatches(haystack[position]))
            {
                position++;
            }

            if (position >= haystack.Length)
            {
                return null;
            }

            int runStart = position;
            while (position < haystack.Length && boundaryRunSegment.AtomMatches(haystack[position]))
            {
                position++;
            }

            int runEnd = position;
            if (runEnd - runStart >= boundaryRunMinimum &&
                runEnd < haystack.Length &&
                boundarySuffixSegment.AtomMatches(haystack[runEnd]) &&
                BoundaryMatches(haystack, runEnd + 1))
            {
                int matchStart = Math.Max(runStart, startOffset);
                int latestStart = runEnd - boundaryRunMinimum;
                if (boundaryStartRequiresUtf8Boundary)
                {
                    matchStart = FirstUtf8BoundaryAtOrAfter(haystack, matchStart, latestStart);
                }

                if (matchStart <= latestStart)
                {
                    return new RegexMatch(matchStart, runEnd + 1 - matchStart);
                }
            }

            position = runEnd + 1;
        }

        return null;
    }

    private bool TryMatchDisjointRunSuffixBoundaryAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            boundaryStartRequiresUtf8Boundary && !RegexByteClass.IsUtf8Boundary(haystack, start) ||
            !boundaryRunSegment.AtomMatches(haystack[start]))
        {
            return false;
        }

        int position = start + 1;
        while (position < haystack.Length && boundaryRunSegment.AtomMatches(haystack[position]))
        {
            position++;
        }

        int runLength = position - start;
        if (runLength < boundaryRunMinimum ||
            position >= haystack.Length ||
            !boundarySuffixSegment.AtomMatches(haystack[position]) ||
            !BoundaryMatches(haystack, position + 1))
        {
            return false;
        }

        length = runLength + 1;
        return true;
    }

    private bool BoundaryMatches(ReadOnlySpan<byte> haystack, int position)
    {
        return RegexByteClass.PredicateMatches(
            haystack,
            position,
            RegexSyntaxKind.WordBoundary,
            boundaryOptions.MultiLine,
            boundaryOptions.Crlf,
            boundaryOptions.LineTerminator,
            boundaryOptions.Utf8,
            boundaryOptions.UnicodeClasses);
    }

    private static bool TryAppendLiteral(
        ReadOnlySpan<byte> literal,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        bool lazy,
        List<RegexSimpleSequenceSegment> segments)
    {
        if (literal.Length == 0)
        {
            return false;
        }

        if (literal.Length == 1)
        {
            segments.Add(new RegexSimpleSequenceSegment(
                RegexSyntaxKind.Literal,
                [literal[0]],
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                minimum,
                maximum,
                lazy));
            return true;
        }

        if (minimum != 1 || maximum != 1)
        {
            return false;
        }

        for (int index = 0; index < literal.Length; index++)
        {
            segments.Add(new RegexSimpleSequenceSegment(
                RegexSyntaxKind.Literal,
                [literal[index]],
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                minimum: 1,
                maximum: 1,
                lazy: false));
        }

        return true;
    }

    private static bool TryAppendAtom(
        RegexAtomNode atom,
        RegexCompileOptions options,
        int minimum,
        int? maximum,
        bool lazy,
        List<RegexSimpleSequenceSegment> segments,
        ref bool sawVariableRepetition)
    {
        if (RegexByteClass.RequiresUtf8ScalarMatch(atom.Kind, atom.Value.Span, options.Utf8, options.CaseInsensitive, options.UnicodeClasses))
        {
            return false;
        }

        sawVariableRepetition |= maximum != minimum;
        segments.Add(new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum,
            maximum,
            lazy));
        return true;
    }

    private static bool TryCreateRepeatedRoot(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexSimpleSequenceEngine? engine)
    {
        engine = null;
        root = UnwrapTransparentGroups(root);
        if (root is not RegexRepetitionNode repetition ||
            !repetition.Maximum.HasValue ||
            repetition.Minimum <= 0 ||
            repetition.Maximum.Value > MaxBoundedRepeat)
        {
            return false;
        }

        var childSegments = new List<RegexSimpleSequenceSegment>();
        bool sawVariableRepetition = false;
        if (!TryAppend(repetition.Child, options, childSegments, ref sawVariableRepetition) ||
            childSegments.Count == 0 ||
            childSegments.Count * repetition.Maximum.Value > MaxSegments ||
            !HasSelectiveRequiredStart(childSegments))
        {
            return false;
        }

        engine = new RegexSimpleSequenceEngine(childSegments, repetition.Minimum, repetition.Maximum.Value, repetition.Lazy);
        return true;
    }

    private static bool TryCreateDisjointRunSuffixBoundary(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexSimpleSequenceEngine? engine)
    {
        engine = null;
        if (!TryUnwrapWithOptions(root, options, out root, out RegexCompileOptions rootOptions) ||
            root is not RegexSequenceNode sequence ||
            !TryCollectSequenceItems(sequence, rootOptions, out List<(RegexSyntaxNode Node, RegexCompileOptions Options)> items) ||
            items.Count != 3 ||
            !TryUnwrapWithOptions(items[0].Node, items[0].Options, out RegexSyntaxNode repeatedNode, out RegexCompileOptions repeatedOptions) ||
            repeatedNode is not RegexRepetitionNode { Minimum: > 0, Maximum: null } repetition ||
            !TryCreateByteSegment(repetition.Child, repeatedOptions, out RegexSimpleSequenceSegment runSegment, out RegexCompileOptions runOptions) ||
            !TryCreateByteSegment(items[1].Node, items[1].Options, out RegexSimpleSequenceSegment suffixSegment, out RegexCompileOptions suffixOptions) ||
            !TryGetWordBoundaryOptions(items[2].Node, items[2].Options, out RegexCompileOptions boundaryOptions) ||
            !AreDisjoint(runSegment, suffixSegment))
        {
            return false;
        }

        engine = new RegexSimpleSequenceEngine(
            runSegment,
            suffixSegment,
            repetition.Minimum,
            boundaryOptions,
            rootOptions.Utf8 && (runOptions.Utf8 || suffixOptions.Utf8 || boundaryOptions.Utf8));
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

    private static bool TryGetWordBoundaryOptions(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexCompileOptions boundaryOptions)
    {
        boundaryOptions = default;
        if (!TryUnwrapWithOptions(node, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions effectiveOptions) ||
            unwrapped is not RegexAtomNode { Kind: RegexSyntaxKind.WordBoundary })
        {
            return false;
        }

        boundaryOptions = effectiveOptions;
        return true;
    }

    private static bool TryCreateByteSegment(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexSimpleSequenceSegment segment,
        out RegexCompileOptions effectiveOptions)
    {
        segment = default;
        effectiveOptions = default;
        if (!TryUnwrapWithOptions(node, options, out RegexSyntaxNode unwrapped, out RegexCompileOptions unwrappedOptions) ||
            unwrapped is not RegexAtomNode atom ||
            !IsByteSegmentAtom(atom) ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                unwrappedOptions.Utf8,
                unwrappedOptions.CaseInsensitive,
                unwrappedOptions.UnicodeClasses))
        {
            return false;
        }

        effectiveOptions = unwrappedOptions;
        segment = new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            effectiveOptions.CaseInsensitive,
            effectiveOptions.MultiLine,
            effectiveOptions.DotMatchesNewline,
            effectiveOptions.Crlf,
            effectiveOptions.LineTerminator,
            minimum: 1,
            maximum: 1,
            lazy: false);
        return !((effectiveOptions.Utf8 || effectiveOptions.UnicodeClasses) && MatchesNonAsciiByte(segment));
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

    private static bool IsByteSegmentAtom(RegexAtomNode atom)
    {
        if (atom.Kind == RegexSyntaxKind.Literal)
        {
            return atom.Value.Length == 1;
        }

        return atom.Kind is RegexSyntaxKind.Dot
            or RegexSyntaxKind.AnyClass
            or RegexSyntaxKind.CharacterClass
            or RegexSyntaxKind.DigitClass
            or RegexSyntaxKind.NotDigitClass
            or RegexSyntaxKind.WordClass
            or RegexSyntaxKind.NotWordClass
            or RegexSyntaxKind.WhitespaceClass
            or RegexSyntaxKind.NotWhitespaceClass;
    }

    private static bool AreDisjoint(RegexSimpleSequenceSegment left, RegexSimpleSequenceSegment right)
    {
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (left.AtomMatches((byte)value) && right.AtomMatches((byte)value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesNonAsciiByte(RegexSimpleSequenceSegment segment)
    {
        for (int value = 0x80; value <= byte.MaxValue; value++)
        {
            if (segment.AtomMatches((byte)value))
            {
                return true;
            }
        }

        return false;
    }

    private static int FirstUtf8BoundaryAtOrAfter(ReadOnlySpan<byte> haystack, int position, int limit)
    {
        while (position <= limit && !RegexByteClass.IsUtf8Boundary(haystack, position))
        {
            position++;
        }

        return position;
    }

    private static bool CanRepeatAtom(RegexAtomNode atom, int? maximum, RegexCompileOptions options)
    {
        if (maximum.HasValue)
        {
            return true;
        }

        return atom.Kind is RegexSyntaxKind.Literal
            or RegexSyntaxKind.DigitClass
            or RegexSyntaxKind.WordClass
            or RegexSyntaxKind.WhitespaceClass ||
            atom.Kind == RegexSyntaxKind.CharacterClass &&
            CountMatchingBytes(atom, options) <= MaxSelectiveStartBytes;
    }

    private static bool HasSelectiveRequiredStart(List<RegexSimpleSequenceSegment> segments)
    {
        return segments.Count > 0 &&
            segments[0].Minimum > 0 &&
            CountMatchingBytes(segments[0]) <= MaxSelectiveStartBytes;
    }

    private static int LongestLiteralRun(List<RegexSimpleSequenceSegment> segments)
    {
        int best = 0;
        int current = 0;
        for (int index = 0; index < segments.Count; index++)
        {
            RegexSimpleSequenceSegment segment = segments[index];
            if (segment.Kind == RegexSyntaxKind.Literal &&
                segment.Minimum == 1 &&
                segment.Maximum == 1)
            {
                current++;
                best = Math.Max(best, current);
            }
            else
            {
                current = 0;
            }
        }

        return best;
    }

    private static bool TryGetFixedWidthLiteralSuffix(
        IReadOnlyList<RegexSimpleSequenceSegment> segments,
        out int width,
        out byte suffixLiteral)
    {
        width = 0;
        suffixLiteral = 0;
        if (segments.Count == 0)
        {
            return false;
        }

        for (int index = 0; index < segments.Count; index++)
        {
            RegexSimpleSequenceSegment segment = segments[index];
            if (segment.Maximum != segment.Minimum)
            {
                width = 0;
                return false;
            }

            width += segment.Minimum;
        }

        RegexSimpleSequenceSegment suffix = segments[^1];
        if (width <= 0 ||
            suffix.Minimum <= 0 ||
            suffix.MatcherKind != RegexSimpleSequenceByteMatcherKind.Literal)
        {
            width = 0;
            return false;
        }

        suffixLiteral = suffix.Literal;
        return true;
    }

    private static bool TryGetLiteralWhitespaceLiteral(
        RegexSimpleSequenceSegment[] segments,
        out byte[] prefix,
        out int whitespaceMinimum,
        out int? whitespaceMaximum,
        out byte[] suffix)
    {
        prefix = [];
        whitespaceMinimum = 0;
        whitespaceMaximum = null;
        suffix = [];
        int whitespaceIndex = -1;
        for (int index = 0; index < segments.Length; index++)
        {
            RegexSimpleSequenceSegment segment = segments[index];
            if (segment.MatcherKind != RegexSimpleSequenceByteMatcherKind.RegexWhitespace)
            {
                continue;
            }

            if (whitespaceIndex >= 0 ||
                segment.Maximum == segment.Minimum ||
                segment.Minimum < 0 ||
                segment.Maximum.HasValue && segment.Maximum.Value < segment.Minimum)
            {
                return false;
            }

            whitespaceIndex = index;
        }

        if (whitespaceIndex <= 0 ||
            whitespaceIndex >= segments.Length - 1)
        {
            return false;
        }

        byte[] collectedPrefix = new byte[whitespaceIndex];
        for (int index = 0; index < whitespaceIndex; index++)
        {
            if (!IsSingleLiteralSegment(segments[index]))
            {
                return false;
            }

            collectedPrefix[index] = segments[index].Literal;
        }

        if (collectedPrefix.Length < MinimumLiteralRunLength)
        {
            return false;
        }

        byte[] collectedSuffix = new byte[segments.Length - whitespaceIndex - 1];
        for (int index = whitespaceIndex + 1; index < segments.Length; index++)
        {
            RegexSimpleSequenceSegment segment = segments[index];
            if (!IsSingleLiteralSegment(segment))
            {
                return false;
            }

            collectedSuffix[index - whitespaceIndex - 1] = segment.Literal;
        }

        if (RegexSimpleSequenceSegment.IsRegexWhitespace(collectedSuffix[0]))
        {
            return false;
        }

        RegexSimpleSequenceSegment whitespace = segments[whitespaceIndex];
        prefix = collectedPrefix;
        whitespaceMinimum = whitespace.Minimum;
        whitespaceMaximum = whitespace.Maximum;
        suffix = collectedSuffix;
        return true;
    }

    private static bool IsSingleLiteralSegment(RegexSimpleSequenceSegment segment)
    {
        return segment is
        {
            MatcherKind: RegexSimpleSequenceByteMatcherKind.Literal,
            Minimum: 1,
            Maximum: 1,
            Lazy: false,
        };
    }

    private static bool TryGetAsciiLetterRunLiteralSuffix(
        RegexSimpleSequenceSegment[] segments,
        out int runMinimum,
        out byte[] suffix)
    {
        runMinimum = 0;
        suffix = [];
        if (segments.Length < 2 ||
            segments[0] is not
            {
                MatcherKind: RegexSimpleSequenceByteMatcherKind.AsciiLetter,
                Minimum: > 0,
                Maximum: null,
                Lazy: false,
            } run)
        {
            return false;
        }

        byte[] literalSuffix = new byte[segments.Length - 1];
        for (int index = 1; index < segments.Length; index++)
        {
            RegexSimpleSequenceSegment segment = segments[index];
            if (segment is not
                {
                    MatcherKind: RegexSimpleSequenceByteMatcherKind.Literal,
                    Minimum: 1,
                    Maximum: 1,
                    Lazy: false,
                } ||
                !RegexSimpleSequenceSegment.IsAsciiLetter(segment.Literal))
            {
                return false;
            }

            literalSuffix[index - 1] = segment.Literal;
        }

        runMinimum = run.Minimum;
        suffix = literalSuffix;
        return true;
    }

    private static int CountMatchingBytes(RegexAtomNode atom, RegexCompileOptions options)
    {
        return CountMatchingBytes(new RegexSimpleSequenceSegment(
            atom.Kind,
            atom.Value.ToArray(),
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            minimum: 1,
            maximum: 1,
            lazy: false));
    }

    private static int CountMatchingBytes(RegexSimpleSequenceSegment segment)
    {
        int count = 0;
        for (int value = 0; value <= 0xFF; value++)
        {
            if (segment.AtomMatches((byte)value))
            {
                count++;
            }
        }

        return count;
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }

}
