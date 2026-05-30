using System;

namespace Scout;

internal sealed class RegexTeddyPrefilter
{
    private const int MinimumPatternCount = 3;
    private const int MaximumPatternCount = 16;
    private const int MaximumPatternLength = 16;

    private readonly byte[][] needles;
    private readonly bool[] firstBytes = new bool[256];

    private RegexTeddyPrefilter(byte[][] needles)
    {
        this.needles = needles;
        for (int index = 0; index < needles.Length; index++)
        {
            firstBytes[needles[index][0]] = true;
        }
    }

    public static bool TryCreate(byte[][] needles, out RegexTeddyPrefilter? prefilter)
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

        prefilter = new RegexTeddyPrefilter(ownedNeedles);
        return true;
    }

    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        for (int position = startAt; position < haystack.Length; position++)
        {
            if (!firstBytes[haystack[position]])
            {
                continue;
            }

            for (int index = 0; index < needles.Length; index++)
            {
                ReadOnlySpan<byte> needle = needles[index];
                if (needle.Length <= haystack.Length - position &&
                    haystack[position + needle.Length - 1] == needle[^1] &&
                    haystack[position..(position + needle.Length)].SequenceEqual(needle))
                {
                    return position;
                }
            }
        }

        return -1;
    }
}
