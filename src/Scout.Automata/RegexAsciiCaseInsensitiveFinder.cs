namespace Scout;

internal sealed class RegexAsciiCaseInsensitiveFinder
{
    private readonly byte[] needle;
    private readonly byte first;
    private readonly byte firstAlternate;
    private readonly bool hasFirstAlternate;

    public RegexAsciiCaseInsensitiveFinder(ReadOnlySpan<byte> needle)
    {
        this.needle = NormalizeAsciiCase(needle);
        if (this.needle.Length == 0)
        {
            return;
        }

        first = this.needle[0];
        hasFirstAlternate = IsAsciiCased(first);
        firstAlternate = hasFirstAlternate ? ToggleAsciiCase(first) : first;
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

        int position = 0;
        while (position <= haystack.Length - needle.Length)
        {
            int offset = hasFirstAlternate
                ? haystack[position..].IndexOfAny(first, firstAlternate)
                : haystack[position..].IndexOf(first);
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

    private bool MatchesAt(ReadOnlySpan<byte> haystack, int position)
    {
        if (needle.Length > haystack.Length - position)
        {
            return false;
        }

        for (int index = 1; index < needle.Length; index++)
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

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }

    private static bool IsAsciiCased(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z';
    }

    private static byte ToggleAsciiCase(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z'
            ? (byte)(value - 0x20)
            : (byte)(value + 0x20);
    }
}
