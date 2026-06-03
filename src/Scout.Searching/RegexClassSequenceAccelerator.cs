using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Scout;

internal sealed class RegexClassSequenceAccelerator
{
    private const int MaxAcceleratedAtoms = 16;
    private const int Word = 0;
    private const int NotWord = 1;
    private const int Digit = 2;
    private const int NotDigit = 3;
    private const int Whitespace = 4;
    private const int NotWhitespace = 5;
    private const int Dot = 6;
    private const int GenericFastPath = 0;
    private const int WhitespaceSeparatedFixedFastPath = 1;
    private const int AlternatingFixedPlusFastPath = 2;
    private const int DisjointFixedPrefixFastPath = 3;

    private static readonly SearchValues<byte> WhitespaceBytes = SearchValues.Create([(byte)' ', (byte)'\t', (byte)'\r', (byte)'\f', (byte)'\v']);
    private readonly int[] kinds;
    private readonly int[] minimums;
    private readonly int[] maximums;
    private readonly int fastPath;

    private RegexClassSequenceAccelerator(int[] kinds, int[] minimums, int[] maximums)
    {
        this.kinds = kinds;
        this.minimums = minimums;
        this.maximums = maximums;
        fastPath = ResolveFastPath(kinds, minimums, maximums);
    }

    public static bool TryCompile(ReadOnlySpan<byte> pattern, out RegexClassSequenceAccelerator? accelerator)
    {
        accelerator = null;
        RegexSyntaxTree tree;
        try
        {
            tree = RegexSyntaxParser.Parse(pattern);
        }
        catch (FormatException)
        {
            return false;
        }

        var kinds = new List<int>();
        var minimums = new List<int>();
        var maximums = new List<int>();
        if (!TryAppendNode(tree.Root, kinds, minimums, maximums) || kinds.Count == 0 || kinds.Count > MaxAcceleratedAtoms)
        {
            return false;
        }

        accelerator = new RegexClassSequenceAccelerator(kinds.ToArray(), minimums.ToArray(), maximums.ToArray());
        return true;
    }

    public bool TryFind(ReadOnlySpan<byte> haystack, int offset, out int matchStart, out int matchLength)
    {
        int start = Math.Clamp(offset, 0, haystack.Length);
        if (fastPath == WhitespaceSeparatedFixedFastPath)
        {
            return TryFindWhitespaceSeparatedFixed(haystack, start, out matchStart, out matchLength);
        }

        if (fastPath == AlternatingFixedPlusFastPath)
        {
            return TryFindAlternatingFixedPlus(haystack, start, out matchStart, out matchLength);
        }

        if (fastPath == DisjointFixedPrefixFastPath)
        {
            return TryFindWithDisjointFixedPrefix(haystack, start, out matchStart, out matchLength);
        }

        for (int index = start; index < haystack.Length; index++)
        {
            if (!Matches(kinds[0], haystack[index]))
            {
                continue;
            }

            if (TryMatchFrom(haystack, index, atomIndex: 0, out int end))
            {
                matchStart = index;
                matchLength = end - index;
                return true;
            }
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    public bool TryFindUnicode(ReadOnlySpan<byte> haystack, int offset, out int matchStart, out int matchLength, out bool completed)
    {
        int start = Math.Clamp(offset, 0, haystack.Length);
        if (fastPath == WhitespaceSeparatedFixedFastPath)
        {
            return TryFindUnicodeWhitespaceSeparatedFixed(haystack, start, out matchStart, out matchLength, out completed);
        }

        int index = start;
        while (index < haystack.Length)
        {
            if (!TryGetScalarLength(haystack, index, out int scalarLength))
            {
                completed = false;
                matchStart = -1;
                matchLength = 0;
                return false;
            }

            if (TryMatchUnicodeFrom(haystack, index, atomIndex: 0, out int end, out completed))
            {
                matchStart = index;
                matchLength = end - index;
                return true;
            }

            if (!completed)
            {
                matchStart = -1;
                matchLength = 0;
                return false;
            }

            index += scalarLength;
        }

        completed = true;
        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private bool TryFindUnicodeWhitespaceSeparatedFixed(
        ReadOnlySpan<byte> haystack,
        int start,
        out int matchStart,
        out int matchLength,
        out bool completed)
    {
        int search = start;
        while (search < haystack.Length)
        {
            if (!TryFindUnicodeKind(haystack, search, Whitespace, out int firstSeparator, out completed))
            {
                matchStart = -1;
                matchLength = 0;
                return false;
            }

            if (!TrySkipUnicodeRun(haystack, firstSeparator, Whitespace, out int firstSeparatorEnd, out completed))
            {
                matchStart = -1;
                matchLength = 0;
                return false;
            }

            if (TryGetUnicodeRunStartBefore(haystack, firstSeparator, kinds[0], minimums[0], start, out int candidate) &&
                TryHasUnicodeRun(haystack, firstSeparatorEnd, kinds[2], minimums[2], out int secondRunEnd, out completed))
            {
                if (!completed)
                {
                    matchStart = -1;
                    matchLength = 0;
                    return false;
                }

                if (TryMatchUnicodeAtom(haystack, secondRunEnd, kinds[3], out _, out completed))
                {
                    if (!TrySkipUnicodeRun(haystack, secondRunEnd, kinds[3], out int secondSeparatorEnd, out completed))
                    {
                        matchStart = -1;
                        matchLength = 0;
                        return false;
                    }

                    if (TryHasUnicodeRun(haystack, secondSeparatorEnd, kinds[4], minimums[4], out int end, out completed))
                    {
                        if (!completed)
                        {
                            matchStart = -1;
                            matchLength = 0;
                            return false;
                        }

                        matchStart = candidate;
                        matchLength = end - candidate;
                        completed = true;
                        return true;
                    }

                    if (!completed)
                    {
                        matchStart = -1;
                        matchLength = 0;
                        return false;
                    }
                }
                else if (!completed)
                {
                    matchStart = -1;
                    matchLength = 0;
                    return false;
                }
            }
            else if (!completed)
            {
                matchStart = -1;
                matchLength = 0;
                return false;
            }

            search = firstSeparatorEnd;
        }

        completed = true;
        matchStart = -1;
        matchLength = 0;
        return false;
    }

    public bool CanUseLineByLineSearch => fastPath == WhitespaceSeparatedFixedFastPath;

    private bool TryFindWhitespaceSeparatedFixed(ReadOnlySpan<byte> haystack, int start, out int matchStart, out int matchLength)
    {
        if (kinds[0] == Word && kinds[2] == Word && kinds[4] == Word)
        {
            return TryFindWordWhitespaceWordWhitespaceWord(haystack, start, out matchStart, out matchLength);
        }

        int search = Math.Min(haystack.Length, start + minimums[0]);
        while (search < haystack.Length)
        {
            int whitespaceOffset = haystack[search..].IndexOfAny(WhitespaceBytes);
            if (whitespaceOffset < 0)
            {
                break;
            }

            int firstSeparator = search + whitespaceOffset;
            int candidate = firstSeparator - minimums[0];
            int nextSearch = SkipRun(haystack, firstSeparator, Whitespace);
            if (candidate >= start &&
                HasRun(haystack, candidate, kinds[0], minimums[0]) &&
                TryMatchTrailingFixedRuns(haystack, candidate, firstSeparator, out int end))
            {
                matchStart = candidate;
                matchLength = end - candidate;
                return true;
            }

            search = Math.Max(firstSeparator + 1, nextSearch);
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private bool TryFindWordWhitespaceWordWhitespaceWord(ReadOnlySpan<byte> haystack, int start, out int matchStart, out int matchLength)
    {
        if (minimums[0] == 5 && minimums[2] == 5 && minimums[4] == 5)
        {
            return TryFindWord5WhitespaceWord5WhitespaceWord5(haystack, start, out matchStart, out matchLength);
        }

        int index = Math.Min(haystack.Length, start + minimums[0]);
        while (index < haystack.Length)
        {
            if (!IsRegexWhitespaceByte(haystack[index]))
            {
                index++;
                continue;
            }

            int firstSeparator = index;
            int candidate = firstSeparator - minimums[0];
            int firstSeparatorEnd = SkipWhitespace(haystack, firstSeparator);
            if (candidate >= start &&
                HasAsciiWordRun(haystack, candidate, minimums[0]) &&
                HasAsciiWordRun(haystack, firstSeparatorEnd, minimums[2]))
            {
                int secondSeparator = firstSeparatorEnd + minimums[2];
                if (secondSeparator < haystack.Length && IsRegexWhitespaceByte(haystack[secondSeparator]))
                {
                    int secondSeparatorEnd = SkipWhitespace(haystack, secondSeparator);
                    if (HasAsciiWordRun(haystack, secondSeparatorEnd, minimums[4]))
                    {
                        matchStart = candidate;
                        matchLength = secondSeparatorEnd + minimums[4] - candidate;
                        return true;
                    }
                }
            }

            index = firstSeparatorEnd;
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private static bool TryFindWord5WhitespaceWord5WhitespaceWord5(ReadOnlySpan<byte> haystack, int start, out int matchStart, out int matchLength)
    {
        int index = Math.Min(haystack.Length, start + 5);
        while (index < haystack.Length)
        {
            if (!IsRegexWhitespaceByte(haystack[index]))
            {
                index++;
                continue;
            }

            int firstSeparator = index;
            int candidate = firstSeparator - 5;
            int firstSeparatorEnd = SkipWhitespace(haystack, firstSeparator);
            if (candidate >= start &&
                HasAsciiWord5(haystack, candidate) &&
                HasAsciiWord5(haystack, firstSeparatorEnd))
            {
                int secondSeparator = firstSeparatorEnd + 5;
                if (secondSeparator < haystack.Length && IsRegexWhitespaceByte(haystack[secondSeparator]))
                {
                    int secondSeparatorEnd = SkipWhitespace(haystack, secondSeparator);
                    if (HasAsciiWord5(haystack, secondSeparatorEnd))
                    {
                        matchStart = candidate;
                        matchLength = secondSeparatorEnd + 5 - candidate;
                        return true;
                    }
                }
            }

            index = firstSeparatorEnd;
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private bool TryMatchTrailingFixedRuns(ReadOnlySpan<byte> haystack, int candidate, int firstSeparator, out int end)
    {
        int firstSeparatorEnd = SkipRun(haystack, firstSeparator, Whitespace);
        if (!HasRun(haystack, firstSeparatorEnd, kinds[2], minimums[2]))
        {
            end = 0;
            return false;
        }

        int secondSeparator = firstSeparatorEnd + minimums[2];
        if (secondSeparator >= haystack.Length || !Matches(Whitespace, haystack[secondSeparator]))
        {
            end = 0;
            return false;
        }

        int secondSeparatorEnd = SkipRun(haystack, secondSeparator, Whitespace);
        if (!HasRun(haystack, secondSeparatorEnd, kinds[4], minimums[4]))
        {
            end = 0;
            return false;
        }

        end = secondSeparatorEnd + minimums[4];
        return end > candidate;
    }

    private bool TryFindAlternatingFixedPlus(ReadOnlySpan<byte> haystack, int start, out int matchStart, out int matchLength)
    {
        int index = start;
        while (index < haystack.Length)
        {
            while (index < haystack.Length && !Matches(kinds[0], haystack[index]))
            {
                index++;
            }

            int firstRunStart = index;
            while (index < haystack.Length && Matches(kinds[0], haystack[index]))
            {
                index++;
            }

            int firstRunEnd = index;
            if (firstRunEnd - firstRunStart < minimums[0])
            {
                continue;
            }

            int candidate = firstRunEnd - minimums[0];
            if (candidate < start || index >= haystack.Length || !Matches(kinds[1], haystack[index]))
            {
                continue;
            }

            while (index < haystack.Length && Matches(kinds[1], haystack[index]))
            {
                index++;
            }

            int secondRunStart = index;
            while (index < haystack.Length && Matches(kinds[2], haystack[index]))
            {
                index++;
            }

            int secondRunEnd = index;
            int secondRunLength = secondRunEnd - secondRunStart;
            if (secondRunLength != minimums[2])
            {
                index = secondRunLength == 0 ? secondRunEnd + 1 : secondRunStart;
                continue;
            }

            if (index >= haystack.Length || !Matches(kinds[3], haystack[index]))
            {
                continue;
            }

            while (index < haystack.Length && Matches(kinds[3], haystack[index]))
            {
                index++;
            }

            int thirdRunStart = index;
            while (index < haystack.Length && Matches(kinds[4], haystack[index]))
            {
                index++;
            }

            if (index - thirdRunStart >= minimums[4])
            {
                matchStart = candidate;
                matchLength = thirdRunStart + minimums[4] - candidate;
                return true;
            }
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private bool TryFindWithDisjointFixedPrefix(ReadOnlySpan<byte> haystack, int start, out int matchStart, out int matchLength)
    {
        int prefixLength = minimums[0];
        int index = start;
        while (index < haystack.Length)
        {
            while (index < haystack.Length && !Matches(kinds[0], haystack[index]))
            {
                index++;
            }

            int runStart = index;
            while (index < haystack.Length && Matches(kinds[0], haystack[index]))
            {
                index++;
            }

            int runEnd = index;
            if (runEnd - runStart < prefixLength)
            {
                continue;
            }

            int candidate = runEnd - prefixLength;
            if (candidate >= start && TryMatchFrom(haystack, candidate, atomIndex: 0, out int end))
            {
                matchStart = candidate;
                matchLength = end - candidate;
                return true;
            }
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private static int ResolveFastPath(int[] kinds, int[] minimums, int[] maximums)
    {
        bool alternatingFixedPlus = kinds.Length == 5 &&
            minimums[0] == maximums[0] &&
            minimums[1] == 1 &&
            maximums[1] == int.MaxValue &&
            minimums[2] == maximums[2] &&
            minimums[3] == 1 &&
            maximums[3] == int.MaxValue &&
            minimums[4] == maximums[4] &&
            !ClassesOverlap(kinds[0], kinds[1]) &&
            !ClassesOverlap(kinds[2], kinds[3]);
        if (alternatingFixedPlus && kinds[1] == Whitespace && kinds[3] == Whitespace)
        {
            return WhitespaceSeparatedFixedFastPath;
        }

        if (alternatingFixedPlus)
        {
            return AlternatingFixedPlusFastPath;
        }

        return kinds.Length > 1 && minimums[0] == maximums[0] && !ClassesOverlap(kinds[0], kinds[1])
            ? DisjointFixedPrefixFastPath
            : GenericFastPath;
    }

    private bool TryMatchFrom(ReadOnlySpan<byte> haystack, int position, int atomIndex, out int end)
    {
        if (atomIndex >= kinds.Length)
        {
            end = position;
            return true;
        }

        int minPosition = position;
        for (int count = 0; count < minimums[atomIndex]; count++)
        {
            if (minPosition >= haystack.Length || !Matches(kinds[atomIndex], haystack[minPosition]))
            {
                end = 0;
                return false;
            }

            minPosition++;
        }

        int maxPosition = minPosition;
        int repetitions = minimums[atomIndex];
        while (repetitions < maximums[atomIndex] &&
            maxPosition < haystack.Length &&
            Matches(kinds[atomIndex], haystack[maxPosition]))
        {
            maxPosition++;
            repetitions++;
        }

        for (int candidate = maxPosition; candidate >= minPosition; candidate--)
        {
            if (TryMatchFrom(haystack, candidate, atomIndex + 1, out end))
            {
                return true;
            }
        }

        end = 0;
        return false;
    }

    private bool TryMatchUnicodeFrom(ReadOnlySpan<byte> haystack, int position, int atomIndex, out int end, out bool completed)
    {
        completed = true;
        if (atomIndex >= kinds.Length)
        {
            end = position;
            return true;
        }

        int minPosition = position;
        for (int count = 0; count < minimums[atomIndex]; count++)
        {
            if (!TryMatchUnicodeAtom(haystack, minPosition, kinds[atomIndex], out int length, out completed))
            {
                end = 0;
                return false;
            }

            minPosition += length;
        }

        int maxPosition = minPosition;
        int repetitions = minimums[atomIndex];
        while (repetitions < maximums[atomIndex] &&
            maxPosition < haystack.Length &&
            TryMatchUnicodeAtom(haystack, maxPosition, kinds[atomIndex], out int length, out completed))
        {
            maxPosition += length;
            repetitions++;
        }

        if (!completed)
        {
            end = 0;
            return false;
        }

        for (int candidate = maxPosition; candidate >= minPosition; candidate = PreviousScalarStart(haystack, minPosition, candidate))
        {
            if (TryMatchUnicodeFrom(haystack, candidate, atomIndex + 1, out end, out completed))
            {
                return true;
            }

            if (!completed)
            {
                end = 0;
                return false;
            }

            if (candidate == minPosition)
            {
                break;
            }
        }

        end = 0;
        return false;
    }

    private static int PreviousScalarStart(ReadOnlySpan<byte> haystack, int minPosition, int position)
    {
        int candidate = position - 1;
        while (candidate > minPosition && (haystack[candidate] & 0xc0) == 0x80)
        {
            candidate--;
        }

        return candidate;
    }

    private static bool TryFindUnicodeKind(
        ReadOnlySpan<byte> haystack,
        int start,
        int kind,
        out int position,
        out bool completed)
    {
        int index = start;
        while (index < haystack.Length)
        {
            if (!TryMatchUnicodeAtom(haystack, index, kind, out int length, out completed))
            {
                if (!completed)
                {
                    position = -1;
                    return false;
                }

                if (!TryGetScalarLength(haystack, index, out length))
                {
                    completed = false;
                    position = -1;
                    return false;
                }
            }
            else
            {
                position = index;
                completed = true;
                return true;
            }

            index += length;
        }

        completed = true;
        position = -1;
        return false;
    }

    private static bool TrySkipUnicodeRun(
        ReadOnlySpan<byte> haystack,
        int start,
        int kind,
        out int end,
        out bool completed)
    {
        completed = true;
        int index = start;
        while (index < haystack.Length &&
            TryMatchUnicodeAtom(haystack, index, kind, out int length, out completed))
        {
            index += length;
        }

        if (!completed)
        {
            end = start;
            return false;
        }

        end = index;
        return index > start;
    }

    private static bool TryHasUnicodeRun(
        ReadOnlySpan<byte> haystack,
        int start,
        int kind,
        int length,
        out int end,
        out bool completed)
    {
        int index = start;
        for (int count = 0; count < length; count++)
        {
            if (!TryMatchUnicodeAtom(haystack, index, kind, out int scalarLength, out completed))
            {
                end = start;
                return false;
            }

            index += scalarLength;
        }

        completed = true;
        end = index;
        return true;
    }

    private static bool TryGetUnicodeRunStartBefore(
        ReadOnlySpan<byte> haystack,
        int end,
        int kind,
        int length,
        int minimumStart,
        out int start)
    {
        int previousEnd = end;
        for (int count = 0; count < length; count++)
        {
            int previousStart = PreviousScalarStart(haystack, minimumStart, previousEnd);
            if (previousStart < minimumStart ||
                !TryMatchUnicodeAtom(haystack, previousStart, kind, out int scalarLength, out bool completed) ||
                !completed ||
                previousStart + scalarLength != previousEnd)
            {
                start = -1;
                return false;
            }

            previousEnd = previousStart;
        }

        start = previousEnd;
        return true;
    }

    private static bool TryAppendNode(RegexSyntaxNode node, List<int> kinds, List<int> minimums, List<int> maximums)
    {
        switch (node)
        {
            case RegexSequenceNode sequence:
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (!TryAppendNode(sequence.Nodes[index], kinds, minimums, maximums))
                    {
                        return false;
                    }
                }

                return true;
            case RegexRepetitionNode repetition:
                if (repetition.Lazy ||
                    repetition.Minimum == 0 ||
                    !TryGetAtomKind(repetition.Child, out int repeatedKind))
                {
                    return false;
                }

                kinds.Add(repeatedKind);
                minimums.Add(repetition.Minimum);
                maximums.Add(repetition.Maximum ?? int.MaxValue);
                return true;
            default:
                if (!TryGetAtomKind(node, out int kind))
                {
                    return false;
                }

                kinds.Add(kind);
                minimums.Add(1);
                maximums.Add(1);
                return true;
        }
    }

    private static bool TryGetAtomKind(RegexSyntaxNode node, out int kind)
    {
        kind = Word;
        if (node is not RegexAtomNode atom)
        {
            return false;
        }

        switch (atom.Kind)
        {
            case RegexSyntaxKind.WordClass:
                kind = Word;
                return true;
            case RegexSyntaxKind.NotWordClass:
                kind = NotWord;
                return true;
            case RegexSyntaxKind.DigitClass:
                kind = Digit;
                return true;
            case RegexSyntaxKind.NotDigitClass:
                kind = NotDigit;
                return true;
            case RegexSyntaxKind.WhitespaceClass:
                kind = Whitespace;
                return true;
            case RegexSyntaxKind.NotWhitespaceClass:
                kind = NotWhitespace;
                return true;
            case RegexSyntaxKind.Dot:
                kind = Dot;
                return true;
            default:
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Matches(int kind, byte value)
    {
        return kind switch
        {
            Word => IsAsciiWordByte(value),
            NotWord => value != (byte)'\n' && !IsAsciiWordByte(value),
            Digit => IsAsciiDigitByte(value),
            NotDigit => value != (byte)'\n' && !IsAsciiDigitByte(value),
            Whitespace => value != (byte)'\n' && IsRegexWhitespaceByte(value),
            NotWhitespace => value != (byte)'\n' && !IsRegexWhitespaceByte(value),
            Dot => value != (byte)'\n',
            _ => false,
        };
    }

    private static bool ClassesOverlap(int left, int right)
    {
        for (int value = 0; value <= 0x7f; value++)
        {
            if (Matches(left, (byte)value) && Matches(right, (byte)value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRun(ReadOnlySpan<byte> haystack, int start, int kind, int length)
    {
        if (start < 0 || length < 0 || start > haystack.Length - length)
        {
            return false;
        }

        for (int index = 0; index < length; index++)
        {
            if (!Matches(kind, haystack[start + index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMatchUnicodeAtom(ReadOnlySpan<byte> haystack, int position, int kind, out int length, out bool completed)
    {
        completed = true;
        length = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        if (!TryDecodeScalar(haystack, position, out Rune rune, out length))
        {
            completed = false;
            return false;
        }

        return MatchesUnicode(kind, rune);
    }

    private static bool TryGetScalarLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        if (haystack[position] <= 0x7f)
        {
            length = 1;
            return true;
        }

        return Rune.DecodeFromUtf8(haystack[position..], out _, out length) == OperationStatus.Done;
    }

    private static bool TryDecodeScalar(ReadOnlySpan<byte> haystack, int position, out Rune rune, out int length)
    {
        rune = default;
        length = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        byte value = haystack[position];
        if (value <= 0x7f)
        {
            rune = new Rune(value);
            length = 1;
            return true;
        }

        return Rune.DecodeFromUtf8(haystack[position..], out rune, out length) == OperationStatus.Done;
    }

    private static bool MatchesUnicode(int kind, Rune value)
    {
        return kind switch
        {
            Word => IsRegexWordRune(value),
            NotWord => value.Value != '\n' && !IsRegexWordRune(value),
            Digit => IsRegexDigitRune(value),
            NotDigit => value.Value != '\n' && !IsRegexDigitRune(value),
            Whitespace => value.Value != '\n' && IsRegexWhitespaceRune(value),
            NotWhitespace => value.Value != '\n' && !IsRegexWhitespaceRune(value),
            Dot => value.Value != '\n',
            _ => false,
        };
    }

    private static bool IsRegexDigitRune(Rune value)
    {
        return value.IsAscii
            ? IsAsciiDigitByte((byte)value.Value)
            : RegexUnicodeTables.IsDecimalNumber(value);
    }

    private static bool IsRegexWordRune(Rune value)
    {
        return value.Value <= 0x052f
            ? IsLowRegexWordScalar(value.Value)
            : RegexUnicodeTables.IsPerlWord(value);
    }

    private static bool IsRegexWhitespaceRune(Rune value)
    {
        return value.IsAscii
            ? IsRegexWhitespaceByte((byte)value.Value)
            : RegexUnicodeTables.IsPerlSpace(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasAsciiWordRun(ReadOnlySpan<byte> haystack, int start, int length)
    {
        if (start < 0 || length < 0 || start > haystack.Length - length)
        {
            return false;
        }

        if (length == 5)
        {
            return IsAsciiWordByte(haystack[start]) &&
                IsAsciiWordByte(haystack[start + 1]) &&
                IsAsciiWordByte(haystack[start + 2]) &&
                IsAsciiWordByte(haystack[start + 3]) &&
                IsAsciiWordByte(haystack[start + 4]);
        }

        for (int index = 0; index < length; index++)
        {
            if (!IsAsciiWordByte(haystack[start + index]))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasAsciiWord5(ReadOnlySpan<byte> haystack, int start)
    {
        return start >= 0 &&
            start <= haystack.Length - 5 &&
            IsAsciiWordByte(haystack[start]) &&
            IsAsciiWordByte(haystack[start + 1]) &&
            IsAsciiWordByte(haystack[start + 2]) &&
            IsAsciiWordByte(haystack[start + 3]) &&
            IsAsciiWordByte(haystack[start + 4]);
    }

    private static int SkipRun(ReadOnlySpan<byte> haystack, int start, int kind)
    {
        int index = start;
        while (index < haystack.Length && Matches(kind, haystack[index]))
        {
            index++;
        }

        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SkipWhitespace(ReadOnlySpan<byte> haystack, int start)
    {
        int index = start;
        while (index < haystack.Length && IsRegexWhitespaceByte(haystack[index]))
        {
            index++;
        }

        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiWordByte(byte value)
    {
        uint foldedAlpha = (uint)((value | 0x20) - (byte)'a');
        uint digit = (uint)(value - (byte)'0');
        return foldedAlpha <= 25 || digit <= 9 || value == (byte)'_';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiDigitByte(byte value)
    {
        return (uint)(value - (byte)'0') <= 9;
    }

    private static bool IsLowRegexWordScalar(int value)
    {
        if (value <= 0x7f)
        {
            return IsAsciiWordByte((byte)value);
        }

        return value is 0x00aa or 0x00b5 or 0x00ba or 0x02ec or 0x02ee or 0x037f or 0x0386 or 0x038c ||
            value is >= 0x00c0 and <= 0x00d6 ||
            value is >= 0x00d8 and <= 0x00f6 ||
            value is >= 0x00f8 and <= 0x02c1 ||
            value is >= 0x02c6 and <= 0x02d1 ||
            value is >= 0x02e0 and <= 0x02e4 ||
            value is >= 0x0300 and <= 0x0374 ||
            value is >= 0x0376 and <= 0x0377 ||
            value is >= 0x037a and <= 0x037d ||
            value is >= 0x0388 and <= 0x038a ||
            value is >= 0x038e and <= 0x03a1 ||
            value is >= 0x03a3 and <= 0x03f5 ||
            value is >= 0x03f7 and <= 0x0481 ||
            value is >= 0x0483 and <= 0x052f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRegexWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\f' or 0x0b;
    }
}
