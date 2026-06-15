namespace Scout;

internal sealed class RegexEmptyEngine
{
    private readonly bool utf8;

    private RegexEmptyEngine(bool utf8)
    {
        this.utf8 = utf8;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexEmptyEngine? engine)
    {
        if (root.Kind == RegexSyntaxKind.Empty)
        {
            engine = new RegexEmptyEngine(options.Utf8);
            return true;
        }

        engine = null;
        return false;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (!utf8)
        {
            return new RegexMatch(start, 0);
        }

        int boundary = FirstUtf8BoundaryAtOrAfter(haystack, start);
        return boundary <= haystack.Length
            ? new RegexMatch(boundary, 0)
            : null;
    }

    public static bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return true;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        if (!utf8)
        {
            return haystack.Length - start + 1L;
        }

        long count = 0;
        for (int position = start; position <= haystack.Length; position++)
        {
            if (RegexByteClass.IsUtf8Boundary(haystack, position))
            {
                count++;
            }
        }

        return count;
    }

    public static long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return 0;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start)
            ? null
            : new RegexMatch(start, 0);
    }

    public RegexMatch? FindEarliest(ReadOnlySpan<byte> haystack, int startAt)
    {
        return Find(haystack, startAt);
    }

    public RegexMatch? FindAllKindAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        return MatchAt(haystack, startAt);
    }

    public IReadOnlyList<RegexMatch> FindOverlappingAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        RegexMatch? match = MatchAt(haystack, startAt);
        return match.HasValue ? [match.Value] : [];
    }

    private static int FirstUtf8BoundaryAtOrAfter(ReadOnlySpan<byte> haystack, int position)
    {
        while (position < haystack.Length && !RegexByteClass.IsUtf8Boundary(haystack, position))
        {
            position++;
        }

        return position;
    }
}
