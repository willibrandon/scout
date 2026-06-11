using System.Buffers;

namespace Scout;

internal sealed class RegexAsciiCaseInsensitiveLiteralSetScanner
{
    private readonly RegexAsciiCaseInsensitiveLiteralSetEntry[][] entriesByAnchorByte;
    private readonly SearchValues<byte> anchorBytes;
    private readonly int maxAnchorIndex;

    public RegexAsciiCaseInsensitiveLiteralSetScanner(IReadOnlyList<byte[]> literals)
    {
        var buckets = new List<RegexAsciiCaseInsensitiveLiteralSetEntry>[256];
        var distinctAnchorBytes = new List<byte>();
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] normalized = NormalizeAsciiCase(literals[index]);
            int anchorIndex = RegexAsciiCaseInsensitiveFinder.SelectAnchorIndex(normalized);
            maxAnchorIndex = Math.Max(maxAnchorIndex, anchorIndex);

            byte anchor = normalized[anchorIndex];
            var entry = new RegexAsciiCaseInsensitiveLiteralSetEntry(index, normalized, anchorIndex);
            AddEntry(buckets, distinctAnchorBytes, anchor, entry);
            if (RegexAsciiCaseInsensitiveFinder.IsAsciiCased(anchor))
            {
                AddEntry(
                    buckets,
                    distinctAnchorBytes,
                    RegexAsciiCaseInsensitiveFinder.ToggleAsciiCase(anchor),
                    entry);
            }
        }

        entriesByAnchorByte = new RegexAsciiCaseInsensitiveLiteralSetEntry[256][];
        for (int index = 0; index < buckets.Length; index++)
        {
            entriesByAnchorByte[index] = buckets[index]?.ToArray() ?? [];
        }

        anchorBytes = SearchValues.Create(distinctAnchorBytes.ToArray());
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

            RegexAsciiCaseInsensitiveLiteralSetEntry[] entries = entriesByAnchorByte[haystack[anchorPosition]];
            for (int index = 0; index < entries.Length; index++)
            {
                RegexAsciiCaseInsensitiveLiteralSetEntry entry = entries[index];
                int start = anchorPosition - entry.AnchorIndex;
                if (start < startOffset ||
                    entry.Literal.Length > haystack.Length - start ||
                    !MatchesAt(haystack, start, entry.Literal))
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

    private static bool MatchesAt(ReadOnlySpan<byte> haystack, int start, byte[] literal)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            if (RegexAsciiCaseInsensitiveFinder.FoldAscii(haystack[start + index]) != literal[index])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] NormalizeAsciiCase(byte[] literal)
    {
        byte[] normalized = literal.ToArray();
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = RegexAsciiCaseInsensitiveFinder.FoldAscii(normalized[index]);
        }

        return normalized;
    }

    private static void AddEntry(
        List<RegexAsciiCaseInsensitiveLiteralSetEntry>[] buckets,
        List<byte> distinctAnchorBytes,
        byte anchor,
        RegexAsciiCaseInsensitiveLiteralSetEntry entry)
    {
        (buckets[anchor] ??= []).Add(entry);
        AddDistinct(distinctAnchorBytes, anchor);
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
