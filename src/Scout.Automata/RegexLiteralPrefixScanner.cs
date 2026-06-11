using System.Buffers;
using System.Runtime.InteropServices;

namespace Scout;

internal sealed class RegexLiteralPrefixScanner
{
    private readonly byte[][] prefixes;
    private readonly SearchValues<byte> firstBytes;

    public RegexLiteralPrefixScanner(IReadOnlyList<byte[]> prefixes)
    {
        this.prefixes = new byte[prefixes.Count][];
        var distinctFirstBytes = new List<byte>();
        for (int index = 0; index < prefixes.Count; index++)
        {
            byte[] prefix = prefixes[index];
            this.prefixes[index] = prefix.ToArray();
            AddDistinct(distinctFirstBytes, prefix[0]);
        }

        firstBytes = SearchValues.Create(CollectionsMarshal.AsSpan(distinctFirstBytes));
    }

    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = Math.Clamp(startAt, 0, haystack.Length);
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(firstBytes);
            if (offset < 0)
            {
                return -1;
            }

            int position = searchAt + offset;
            for (int index = 0; index < prefixes.Length; index++)
            {
                ReadOnlySpan<byte> prefix = prefixes[index];
                if (prefix.Length <= haystack.Length - position &&
                    haystack[position + prefix.Length - 1] == prefix[^1] &&
                    haystack[position..(position + prefix.Length)].SequenceEqual(prefix))
                {
                    return position;
                }
            }

            searchAt = position + 1;
        }

        return -1;
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
