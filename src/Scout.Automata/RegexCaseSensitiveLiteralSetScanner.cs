using System.Buffers;

namespace Scout;

internal sealed class RegexCaseSensitiveLiteralSetScanner
{
    private const int MaxLiteralCount = 8;

    private readonly RegexCaseSensitiveLiteralSetEntry[][] entriesByAnchorByte;
    private readonly SearchValues<byte> anchorBytes;
    private readonly int maxAnchorIndex;

    private RegexCaseSensitiveLiteralSetScanner(IReadOnlyList<byte[]> literals)
    {
        var buckets = new List<RegexCaseSensitiveLiteralSetEntry>[256];
        var distinctAnchorBytes = new List<byte>();
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] literal = literals[index].ToArray();
            int anchorIndex = SelectAnchorIndex(literal);
            maxAnchorIndex = Math.Max(maxAnchorIndex, anchorIndex);

            byte anchor = literal[anchorIndex];
            (buckets[anchor] ??= []).Add(new RegexCaseSensitiveLiteralSetEntry(index, literal, anchorIndex));
            AddDistinct(distinctAnchorBytes, anchor);
        }

        entriesByAnchorByte = new RegexCaseSensitiveLiteralSetEntry[256][];
        for (int index = 0; index < buckets.Length; index++)
        {
            entriesByAnchorByte[index] = buckets[index]?.ToArray() ?? [];
        }

        anchorBytes = SearchValues.Create(distinctAnchorBytes.ToArray());
    }

    public static bool TryCreate(IReadOnlyList<byte[]> literals, out RegexCaseSensitiveLiteralSetScanner? scanner)
    {
        scanner = null;
        if (literals.Count is <= 1 or > MaxLiteralCount)
        {
            return false;
        }

        if (!ContainsTwoByteUtf8NonAsciiOnly(literals))
        {
            return false;
        }

        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].Length == 0)
            {
                return false;
            }
        }

        scanner = new RegexCaseSensitiveLiteralSetScanner(literals);
        return true;
    }

    private static bool ContainsTwoByteUtf8NonAsciiOnly(IReadOnlyList<byte[]> literals)
    {
        bool containsNonAscii = false;
        for (int literalIndex = 0; literalIndex < literals.Count; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            int index = 0;
            while (index < literal.Length)
            {
                byte value = literal[index];
                if (value <= 0x7F)
                {
                    index++;
                    continue;
                }

                containsNonAscii = true;
                if (value is < 0xC2 or > 0xDF ||
                    index + 1 >= literal.Length ||
                    literal[index + 1] is < 0x80 or > 0xBF)
                {
                    return false;
                }

                index += 2;
            }
        }

        return containsNonAscii;
    }

    public RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = startOffset;
        RegexLiteralSetCandidate? best = null;
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(anchorBytes);
            if (offset < 0)
            {
                break;
            }

            int anchorPosition = searchAt + offset;
            if (best.HasValue && anchorPosition - maxAnchorIndex > best.Value.Match.Start)
            {
                break;
            }

            RegexCaseSensitiveLiteralSetEntry[] entries = entriesByAnchorByte[haystack[anchorPosition]];
            for (int index = 0; index < entries.Length; index++)
            {
                RegexCaseSensitiveLiteralSetEntry entry = entries[index];
                int start = anchorPosition - entry.AnchorIndex;
                if (start < startOffset ||
                    entry.Literal.Length > haystack.Length - start ||
                    haystack[start + entry.Literal.Length - 1] != entry.Literal[^1] ||
                    !haystack.Slice(start, entry.Literal.Length).SequenceEqual(entry.Literal))
                {
                    continue;
                }

                var candidate = new RegexLiteralSetCandidate(
                    entry.LiteralId,
                    new RegexMatch(start, entry.Literal.Length));
                if (IsBetter(candidate, best))
                {
                    best = candidate;
                }
            }

            searchAt = anchorPosition + 1;
        }

        return best;
    }

    private static int SelectAnchorIndex(ReadOnlySpan<byte> literal)
    {
        int bestIndex = 0;
        int bestScore = AnchorScore(literal[0]);
        for (int index = 1; index < literal.Length; index++)
        {
            int score = AnchorScore(literal[index]);
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
        if (value is >= 0x80 and <= 0xBF)
        {
            return 260;
        }

        if (value >= 0xC2)
        {
            return 80;
        }

        if (value is >= (byte)'0' and <= (byte)'9')
        {
            return 180;
        }

        byte folded = RegexAsciiCaseInsensitiveFinder.FoldAscii(value);
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

    private static bool IsBetter(RegexLiteralSetCandidate candidate, RegexLiteralSetCandidate? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        RegexLiteralSetCandidate current = best.Value;
        if (candidate.Match.Start != current.Match.Start)
        {
            return candidate.Match.Start < current.Match.Start;
        }

        return candidate.LiteralId < current.LiteralId;
    }
}
