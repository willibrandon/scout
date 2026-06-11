namespace Scout;

internal sealed class RegexAsciiCaseInsensitiveFinder
{
    private readonly byte[] needle;
    private readonly int anchorIndex;
    private readonly byte anchor;
    private readonly byte anchorAlternate;
    private readonly bool hasAnchorAlternate;

    public RegexAsciiCaseInsensitiveFinder(ReadOnlySpan<byte> needle)
    {
        this.needle = NormalizeAsciiCase(needle);
        if (this.needle.Length == 0)
        {
            return;
        }

        anchorIndex = SelectAnchorIndex(this.needle);
        anchor = this.needle[anchorIndex];
        hasAnchorAlternate = IsAsciiCased(anchor);
        anchorAlternate = hasAnchorAlternate ? ToggleAsciiCase(anchor) : anchor;
    }

    public int Find(ReadOnlySpan<byte> haystack)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        if (needle.Length > haystack.Length)
        {
            return -1;
        }

        int anchorPosition = anchorIndex;
        int lastAnchorPosition = haystack.Length - (needle.Length - anchorIndex);
        while (anchorPosition <= lastAnchorPosition)
        {
            int offset = hasAnchorAlternate
                ? haystack[anchorPosition..].IndexOfAny(anchor, anchorAlternate)
                : haystack[anchorPosition..].IndexOf(anchor);
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

    private bool MatchesAt(ReadOnlySpan<byte> haystack, int position)
    {
        if (needle.Length > haystack.Length - position)
        {
            return false;
        }

        for (int index = 0; index < needle.Length; index++)
        {
            if (FoldAscii(haystack[position + index]) != needle[index])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] NormalizeAsciiCase(ReadOnlySpan<byte> value)
    {
        byte[] normalized = value.ToArray();
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = FoldAscii(normalized[index]);
        }

        return normalized;
    }

    internal static int SelectAnchorIndex(ReadOnlySpan<byte> value)
    {
        int bestIndex = 0;
        int bestScore = AnchorScore(value[0]);
        for (int index = 1; index < value.Length; index++)
        {
            int score = AnchorScore(value[index]);
            if (score > bestScore)
            {
                bestIndex = index;
                bestScore = score;
            }
        }

        return bestIndex;
    }

    private static int AnchorScore(byte value)
    {
        byte folded = FoldAscii(value);
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

    internal static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }

    internal static bool IsAsciiCased(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    internal static byte ToggleAsciiCase(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z'
            ? (byte)(value - 0x20)
            : (byte)(value + 0x20);
    }
}
