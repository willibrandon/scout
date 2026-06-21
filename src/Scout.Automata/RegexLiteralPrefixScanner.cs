using System.Buffers;
using System.Runtime.InteropServices;

namespace Scout;

internal sealed class RegexLiteralPrefixScanner
{
    private readonly byte[][] prefixes;
    private readonly byte[][][] prefixesByAnchorByte;
    private readonly SearchValues<byte> anchorBytes;
    private readonly int anchorOffset;

    public RegexLiteralPrefixScanner(IReadOnlyList<byte[]> prefixes)
    {
        this.prefixes = new byte[prefixes.Count][];
        for (int index = 0; index < prefixes.Count; index++)
        {
            byte[] prefix = prefixes[index];
            this.prefixes[index] = prefix.ToArray();
        }

        anchorOffset = SelectAnchorOffset(this.prefixes);
        var distinctAnchorBytes = new List<byte>();
        var buckets = new List<byte[]>[256];
        for (int index = 0; index < this.prefixes.Length; index++)
        {
            byte anchor = this.prefixes[index][anchorOffset];
            AddDistinct(distinctAnchorBytes, anchor);
            (buckets[anchor] ??= []).Add(this.prefixes[index]);
        }

        prefixesByAnchorByte = new byte[256][][];
        for (int index = 0; index < buckets.Length; index++)
        {
            prefixesByAnchorByte[index] = buckets[index]?.ToArray() ?? [];
        }

        anchorBytes = SearchValues.Create(CollectionsMarshal.AsSpan(distinctAnchorBytes));
    }

    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        int candidateStartAt = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = Math.Min(candidateStartAt + anchorOffset, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(anchorBytes);
            if (offset < 0)
            {
                return -1;
            }

            int anchor = searchAt + offset;
            int position = anchor - anchorOffset;
            if (position < candidateStartAt)
            {
                searchAt = anchor + 1;
                continue;
            }

            byte[][] candidatePrefixes = prefixesByAnchorByte[haystack[anchor]];
            for (int index = 0; index < candidatePrefixes.Length; index++)
            {
                ReadOnlySpan<byte> prefix = candidatePrefixes[index];
                if (prefix.Length <= haystack.Length - position &&
                    haystack[position + prefix.Length - 1] == prefix[^1] &&
                    haystack[position..(position + prefix.Length)].SequenceEqual(prefix))
                {
                    return position;
                }
            }

            searchAt = anchor + 1;
        }

        return -1;
    }

    private static int SelectAnchorOffset(byte[][] prefixes)
    {
        int minLength = int.MaxValue;
        for (int index = 0; index < prefixes.Length; index++)
        {
            minLength = Math.Min(minLength, prefixes[index].Length);
        }

        int bestOffset = 0;
        int bestScore = int.MinValue;
        for (int offset = 0; offset < minLength; offset++)
        {
            var distinct = new List<byte>();
            int utf8LeadingBytes = 0;
            for (int index = 0; index < prefixes.Length; index++)
            {
                byte value = prefixes[index][offset];
                AddDistinct(distinct, value);
                if (IsUtf8LeadingByte(value))
                {
                    utf8LeadingBytes++;
                }
            }

            int score = distinct.Count * 4 - utf8LeadingBytes;
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }

    private static bool IsUtf8LeadingByte(byte value)
    {
        return value is >= 0xC2 and <= 0xF4;
    }

    private static void AddDistinct(List<byte> bytes, byte value)
    {
        for (int index = 0; index < bytes.Count; index++)
        {
            if (bytes[index] == value)
            {
                return;
            }
        }

        bytes.Add(value);
    }
}
