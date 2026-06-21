namespace Scout;

internal sealed class RegexTeddyPrefilter
{
    private const int MinimumPatternCount = 3;
    private const int MaximumPatternCount = 16;
    private const int MaximumPatternLength = 16;

    private readonly byte[][] needles;
    private readonly bool[] firstBytes = new bool[256];
    private readonly byte[] distinctFirstBytes;
    private readonly byte[] commonOffsets;
    private readonly byte[] commonBytes;
    private readonly bool asciiCaseInsensitive;

    private RegexTeddyPrefilter(byte[][] needles, bool asciiCaseInsensitive)
    {
        this.needles = needles;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        var distinct = new List<byte>();
        for (int index = 0; index < needles.Length; index++)
        {
            AddFirstByteVariants(needles[index][0], distinct);
        }

        distinctFirstBytes = distinct.ToArray();
        BuildCommonByteChecks(needles, asciiCaseInsensitive, out commonOffsets, out commonBytes);
    }

    public static bool TryCreate(byte[][] needles, out RegexTeddyPrefilter? prefilter)
    {
        return TryCreate(needles, asciiCaseInsensitive: false, out prefilter);
    }

    public static bool TryCreate(byte[][] needles, bool asciiCaseInsensitive, out RegexTeddyPrefilter? prefilter)
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

        prefilter = new RegexTeddyPrefilter(ownedNeedles, asciiCaseInsensitive);
        return true;
    }

    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        startAt = Math.Clamp(startAt, 0, haystack.Length);
        if (distinctFirstBytes.Length == 1)
        {
            return FindCandidateByFirstByte(haystack, startAt, distinctFirstBytes[0]);
        }

        if (distinctFirstBytes.Length == 2)
        {
            return FindCandidateByFirstBytePair(haystack, startAt, distinctFirstBytes[0], distinctFirstBytes[1]);
        }

        for (int position = startAt; position < haystack.Length; position++)
        {
            if (!firstBytes[haystack[position]])
            {
                continue;
            }

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

    private int FindCandidateByFirstBytePair(ReadOnlySpan<byte> haystack, int startAt, byte firstByte, byte secondByte)
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

        for (int index = 0; index < needles.Length; index++)
        {
            ReadOnlySpan<byte> needle = needles[index];
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
        for (int index = 0; index < commonOffsets.Length; index++)
        {
            int at = position + commonOffsets[index];
            if ((uint)at >= (uint)haystack.Length ||
                !ByteEquals(haystack[at], commonBytes[index]))
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
            asciiCaseInsensitive &&
            IsAsciiCased(left) &&
            FoldAscii(left) == FoldAscii(right);
    }

    private void AddFirstByteVariants(byte value, List<byte> distinct)
    {
        AddFirstByte(value, distinct);
        if (!asciiCaseInsensitive || !IsAsciiCased(value))
        {
            return;
        }

        AddFirstByte(FoldAscii(value), distinct);
        AddFirstByte(ToggleAsciiCase(value), distinct);
    }

    private void AddFirstByte(byte value, List<byte> distinct)
    {
        firstBytes[value] = true;
        if (!distinct.Contains(value))
        {
            distinct.Add(value);
        }
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
