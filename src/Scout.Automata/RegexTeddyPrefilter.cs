using System.Buffers;

namespace Scout;

/// <summary>
/// Finds candidates for a small set of literal regex prefixes.
/// </summary>
internal sealed class RegexTeddyPrefilter(
    byte[][] needles,
    bool asciiCaseInsensitive,
    byte[] distinctFirstBytes,
    SearchValues<byte> firstByteSearch,
    int[][] patternIndexesByFirstByte,
    byte[] commonOffsets,
    byte[] commonBytes)
{
    private const int MinimumPatternCount = 3;
    private const int MaximumPatternCount = 16;
    private const int MaximumPatternLength = 16;

    private readonly byte[][] _needles = needles;
    private readonly bool _asciiCaseInsensitive = asciiCaseInsensitive;
    private readonly byte[] _distinctFirstBytes = distinctFirstBytes;
    private readonly SearchValues<byte> _firstByteSearch = firstByteSearch;
    private readonly int[][] _patternIndexesByFirstByte = patternIndexesByFirstByte;
    private readonly byte[] _commonOffsets = commonOffsets;
    private readonly byte[] _commonBytes = commonBytes;

    /// <summary>
    /// Attempts to create a case-sensitive prefilter for a small literal set.
    /// </summary>
    /// <param name="needles">The literal prefixes.</param>
    /// <param name="prefilter">Receives the prefilter when the literal set is supported.</param>
    /// <returns><see langword="true" /> when the prefilter was created.</returns>
    public static bool TryCreate(byte[][] needles, out RegexTeddyPrefilter? prefilter)
    {
        return TryCreate(needles, asciiCaseInsensitive: false, out prefilter);
    }

    /// <summary>
    /// Attempts to create a prefilter for a small literal set.
    /// </summary>
    /// <param name="needles">The literal prefixes.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII letters compare case-insensitively.</param>
    /// <param name="prefilter">Receives the prefilter when the literal set is supported.</param>
    /// <returns><see langword="true" /> when the prefilter was created.</returns>
    public static bool TryCreate(
        byte[][] needles,
        bool asciiCaseInsensitive,
        out RegexTeddyPrefilter? prefilter)
    {
        if (needles.Length is < MinimumPatternCount or > MaximumPatternCount)
        {
            prefilter = null;
            return false;
        }

        byte[][] ownedNeedles = new byte[needles.Length][];
        for (int index = 0; index < needles.Length; index++)
        {
            byte[] needle = needles[index];
            if (needle.Length is 0 or > MaximumPatternLength)
            {
                prefilter = null;
                return false;
            }

            ownedNeedles[index] = needle.ToArray();
        }

        BuildFirstByteData(
            ownedNeedles,
            asciiCaseInsensitive,
            out byte[] distinctFirstBytes,
            out int[][] patternIndexesByFirstByte);
        BuildCommonByteChecks(
            ownedNeedles,
            asciiCaseInsensitive,
            out byte[] commonOffsets,
            out byte[] commonBytes);
        prefilter = new RegexTeddyPrefilter(
            ownedNeedles,
            asciiCaseInsensitive,
            distinctFirstBytes,
            SearchValues.Create(distinctFirstBytes),
            patternIndexesByFirstByte,
            commonOffsets,
            commonBytes);
        return true;
    }

    /// <summary>
    /// Finds the next literal-prefix candidate at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first byte offset to inspect.</param>
    /// <returns>The candidate offset, or <c>-1</c> when none remains.</returns>
    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        startAt = Math.Clamp(startAt, 0, haystack.Length);
        if (_distinctFirstBytes.Length == 1)
        {
            return FindCandidateByFirstByte(haystack, startAt, _distinctFirstBytes[0]);
        }

        if (_distinctFirstBytes.Length == 2)
        {
            return FindCandidateByFirstBytePair(
                haystack,
                startAt,
                _distinctFirstBytes[0],
                _distinctFirstBytes[1]);
        }

        for (int position = startAt; position < haystack.Length; position++)
        {
            int offset = haystack[position..].IndexOfAny(_firstByteSearch);
            if (offset < 0)
            {
                return -1;
            }

            position += offset;
            if (MatchesAt(haystack, position))
            {
                return position;
            }
        }

        return -1;
    }

    private int FindCandidateByFirstByte(ReadOnlySpan<byte> haystack, int startAt, byte firstByte)
    {
        for (int position = startAt; position < haystack.Length;)
        {
            int offset = haystack[position..].IndexOf(firstByte);
            if (offset < 0)
            {
                return -1;
            }

            position += offset;
            if (MatchesAt(haystack, position))
            {
                return position;
            }

            position++;
        }

        return -1;
    }

    private int FindCandidateByFirstBytePair(
        ReadOnlySpan<byte> haystack,
        int startAt,
        byte firstByte,
        byte secondByte)
    {
        for (int position = startAt; position < haystack.Length;)
        {
            int offset = haystack[position..].IndexOfAny(firstByte, secondByte);
            if (offset < 0)
            {
                return -1;
            }

            position += offset;
            if (MatchesAt(haystack, position))
            {
                return position;
            }

            position++;
        }

        return -1;
    }

    private static void BuildFirstByteData(
        byte[][] needles,
        bool asciiCaseInsensitive,
        out byte[] distinctFirstBytes,
        out int[][] patternIndexesByFirstByte)
    {
        var distinct = new List<byte>();
        var indexes = new List<int>?[256];
        for (int patternIndex = 0; patternIndex < needles.Length; patternIndex++)
        {
            byte value = needles[patternIndex][0];
            AddFirstByte(value, patternIndex, distinct, indexes);
            if (asciiCaseInsensitive && IsAsciiCased(value))
            {
                AddFirstByte(FoldAscii(value), patternIndex, distinct, indexes);
                AddFirstByte(ToggleAsciiCase(value), patternIndex, distinct, indexes);
            }
        }

        patternIndexesByFirstByte = new int[256][];
        for (int index = 0; index < patternIndexesByFirstByte.Length; index++)
        {
            patternIndexesByFirstByte[index] = indexes[index]?.ToArray() ?? [];
        }

        distinctFirstBytes = distinct.ToArray();
    }

    private static void AddFirstByte(
        byte value,
        int patternIndex,
        List<byte> distinct,
        List<int>?[] indexes)
    {
        List<int> bucket = indexes[value] ??= [];
        if (!bucket.Contains(patternIndex))
        {
            bucket.Add(patternIndex);
        }

        if (!distinct.Contains(value))
        {
            distinct.Add(value);
        }
    }

    private static void BuildCommonByteChecks(
        byte[][] needles,
        bool asciiCaseInsensitive,
        out byte[] commonOffsets,
        out byte[] commonBytes)
    {
        int minLength = int.MaxValue;
        for (int index = 0; index < needles.Length; index++)
        {
            minLength = Math.Min(minLength, needles[index].Length);
        }

        var offsets = new List<byte>();
        var bytes = new List<byte>();
        for (int offset = 1; offset < minLength; offset++)
        {
            byte common = NormalizeAsciiCase(needles[0][offset], asciiCaseInsensitive);
            bool allCommon = true;
            for (int index = 1; index < needles.Length; index++)
            {
                if (NormalizeAsciiCase(needles[index][offset], asciiCaseInsensitive) != common)
                {
                    allCommon = false;
                    break;
                }
            }

            if (allCommon)
            {
                offsets.Add((byte)offset);
                bytes.Add(common);
            }
        }

        commonOffsets = offsets.ToArray();
        commonBytes = bytes.ToArray();
    }

    private bool MatchesAt(ReadOnlySpan<byte> haystack, int position)
    {
        if (!CommonBytesMatch(haystack, position))
        {
            return false;
        }

        ReadOnlySpan<int> patternIndexes = _patternIndexesByFirstByte[haystack[position]];
        for (int index = 0; index < patternIndexes.Length; index++)
        {
            ReadOnlySpan<byte> needle = _needles[patternIndexes[index]];
            if (needle.Length <= haystack.Length - position &&
                ByteEquals(haystack[position + needle.Length - 1], needle[^1]) &&
                MatchesNeedle(haystack[position..(position + needle.Length)], needle))
            {
                return true;
            }
        }

        return false;
    }

    private bool CommonBytesMatch(ReadOnlySpan<byte> haystack, int position)
    {
        for (int index = 0; index < _commonOffsets.Length; index++)
        {
            int at = position + _commonOffsets[index];
            if ((uint)at >= (uint)haystack.Length ||
                !ByteEquals(haystack[at], _commonBytes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesNeedle(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (int index = 0; index < needle.Length; index++)
        {
            if (!ByteEquals(haystack[index], needle[index]))
            {
                return false;
            }
        }

        return true;
    }

    private bool ByteEquals(byte left, byte right)
    {
        return left == right ||
            _asciiCaseInsensitive &&
            IsAsciiCased(left) &&
            FoldAscii(left) == FoldAscii(right);
    }

    private static byte NormalizeAsciiCase(byte value, bool asciiCaseInsensitive)
    {
        return asciiCaseInsensitive ? FoldAscii(value) : value;
    }

    private static bool IsAsciiCased(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }

    private static byte ToggleAsciiCase(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z'
            ? (byte)(value - 32)
            : (byte)(value + 32);
    }
}
