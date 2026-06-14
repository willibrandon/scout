namespace Scout;

internal sealed class RegexAnchoredLiteralFinder
{
    private const int MinimumNeedleLength = 4;
    private const int MinimumAnchorAdvantage = 80;
    private const int MinimumAsciiThreeByteAnchorScore = 250;

    private readonly byte[] needle;
    private readonly int anchorIndex;
    private readonly byte anchor;

    private RegexAnchoredLiteralFinder(ReadOnlySpan<byte> needle, int anchorIndex)
    {
        this.needle = needle.ToArray();
        this.anchorIndex = anchorIndex;
        anchor = this.needle[anchorIndex];
    }

    public static bool TryCreate(ReadOnlySpan<byte> needle, out RegexAnchoredLiteralFinder? finder)
    {
        finder = null;
        bool containsNonAscii = ContainsNonAscii(needle);
        bool threeByteAscii = !containsNonAscii && needle.Length == 3;
        if (containsNonAscii && needle.Length < MinimumNeedleLength ||
            !containsNonAscii && !threeByteAscii ||
            !ContainsAsciiOrTwoByteUtf8Only(needle))
        {
            return false;
        }

        int anchorIndex = SelectAnchorIndex(needle);
        bool firstByteAsciiAnchor = threeByteAscii &&
            AsciiAnchorScore(needle[0]) >= MinimumAsciiThreeByteAnchorScore;
        if (anchorIndex == 0 && !firstByteAsciiAnchor ||
            anchorIndex != 0 && AnchorScore(needle, anchorIndex) < AnchorScore(needle, 0) + MinimumAnchorAdvantage)
        {
            return false;
        }

        finder = new RegexAnchoredLiteralFinder(needle, anchorIndex);
        return true;
    }

    public int Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        if (needle.Length == 0)
        {
            return Math.Clamp(startAt, 0, haystack.Length);
        }

        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (needle.Length > haystack.Length - startOffset)
        {
            return -1;
        }

        int anchorPosition = startOffset + anchorIndex;
        int lastAnchorPosition = haystack.Length - (needle.Length - anchorIndex);
        while (anchorPosition <= lastAnchorPosition)
        {
            int searchLength = lastAnchorPosition - anchorPosition + 1;
            int offset = haystack.Slice(anchorPosition, searchLength).IndexOf(anchor);
            if (offset < 0)
            {
                return -1;
            }

            anchorPosition += offset;
            int matchStart = anchorPosition - anchorIndex;
            if (MatchesAt(haystack, matchStart))
            {
                return matchStart;
            }

            anchorPosition++;
        }

        return -1;
    }

    public long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int position = Math.Clamp(startAt, 0, haystack.Length);
        while (position <= haystack.Length)
        {
            int matchStart = Find(haystack, position);
            if (matchStart < 0)
            {
                return total;
            }

            total += sumSpans ? needle.Length : 1;
            position = matchStart + needle.Length;
        }

        return total;
    }

    private bool MatchesAt(ReadOnlySpan<byte> haystack, int position)
    {
        return needle.Length <= haystack.Length - position &&
            haystack[position] == needle[0] &&
            haystack[position + needle.Length - 1] == needle[^1] &&
            haystack.Slice(position, needle.Length).SequenceEqual(needle);
    }

    private static int SelectAnchorIndex(ReadOnlySpan<byte> value)
    {
        int bestIndex = 0;
        int bestScore = AnchorScore(value, 0);
        for (int index = 1; index < value.Length; index++)
        {
            int score = AnchorScore(value, index);
            if (score > bestScore)
            {
                bestIndex = index;
                bestScore = score;
            }
        }

        return bestIndex;
    }

    private static int AnchorScore(ReadOnlySpan<byte> value, int index)
    {
        byte current = value[index];
        if (current <= 0x7F)
        {
            return AsciiAnchorScore(current);
        }

        if (IsUtf8Continuation(current) && index > 0)
        {
            return ContinuationAnchorScore(value[index - 1], current);
        }

        return IsUtf8LeadByte(current) ? 30 : 240;
    }

    private static int ContinuationAnchorScore(byte previous, byte current)
    {
        if (previous == 0xD0)
        {
            if (current <= 0xAF)
            {
                return 640;
            }

            return IsCommonCyrillicContinuation(current) ? 280 : 380;
        }

        if (previous == 0xD1)
        {
            return IsCommonCyrillicContinuation(current) ? 280 : 380;
        }

        return 420 + ((current * 17) & 0x3F);
    }

    internal static int AsciiAnchorScore(byte value)
    {
        byte folded = RegexAsciiCaseInsensitiveFinder.FoldAscii(value);
        if (folded is >= (byte)'0' and <= (byte)'9')
        {
            return 180;
        }

        if (folded is < (byte)'a' or > (byte)'z')
        {
            return folded == (byte)' ' ? 10 : 220;
        }

        return folded switch
        {
            (byte)'q' or (byte)'z' => 260,
            (byte)'x' or (byte)'j' => 250,
            (byte)'k' => 240,
            (byte)'v' => 230,
            (byte)'b' or (byte)'p' => 220,
            (byte)'g' => 210,
            (byte)'w' => 200,
            (byte)'y' => 190,
            (byte)'f' => 180,
            (byte)'m' => 170,
            (byte)'c' => 160,
            (byte)'u' => 150,
            (byte)'l' => 140,
            (byte)'d' => 130,
            (byte)'r' => 120,
            (byte)'h' => 110,
            (byte)'s' => 100,
            (byte)'n' => 90,
            (byte)'i' => 80,
            (byte)'o' => 70,
            (byte)'a' => 60,
            (byte)'t' => 50,
            (byte)'e' => 40,
            _ => 30,
        };
    }

    private static bool IsCommonCyrillicContinuation(byte value)
    {
        return value is 0x80 or 0x81 or 0x82 or 0x83 or 0x8C
            or 0xB0 or 0xB5 or 0xB8 or 0xBB or 0xBC or 0xBD or 0xBE;
    }

    private static bool ContainsNonAscii(ReadOnlySpan<byte> value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] > 0x7F)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAsciiOrTwoByteUtf8Only(ReadOnlySpan<byte> value)
    {
        int index = 0;
        while (index < value.Length)
        {
            byte current = value[index];
            if (current <= 0x7F)
            {
                index++;
                continue;
            }

            if (current is < 0xC2 or > 0xDF ||
                index + 1 >= value.Length ||
                value[index + 1] is < 0x80 or > 0xBF)
            {
                return false;
            }

            index += 2;
        }

        return true;
    }

    private static bool IsUtf8Continuation(byte value)
    {
        return value is >= 0x80 and <= 0xBF;
    }

    private static bool IsUtf8LeadByte(byte value)
    {
        return value is >= 0xC2 and <= 0xF4;
    }
}
