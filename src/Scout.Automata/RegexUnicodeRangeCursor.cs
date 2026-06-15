namespace Scout;

internal ref struct RegexUnicodeRangeCursor
{
    private readonly ReadOnlySpan<int> ranges;
    private int pairIndex;

    public RegexUnicodeRangeCursor(ReadOnlySpan<int> ranges)
    {
        this.ranges = ranges;
        pairIndex = 0;
    }

    public bool Contains(int value)
    {
        if (ranges.Length == 0)
        {
            return false;
        }

        int rangeStart = ranges[pairIndex];
        int rangeEnd = ranges[pairIndex + 1];
        if (value >= rangeStart && value <= rangeEnd)
        {
            return true;
        }

        if (value > rangeEnd)
        {
            int index = pairIndex + 2;
            if (index >= ranges.Length)
            {
                return false;
            }

            if (value <= ranges[index + 1])
            {
                if (value >= ranges[index])
                {
                    pairIndex = index;
                    return true;
                }

                return false;
            }

            return ContainsByBinarySearch(value);
        }

        return ContainsByBinarySearch(value);
    }

    private bool ContainsByBinarySearch(int value)
    {
        int low = 0;
        int high = (ranges.Length / 2) - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int start = ranges[middle * 2];
            int end = ranges[(middle * 2) + 1];
            if (value < start)
            {
                high = middle - 1;
                continue;
            }

            if (value > end)
            {
                low = middle + 1;
                continue;
            }

            pairIndex = middle * 2;
            return true;
        }

        return false;
    }

    public static int[] MergeRanges(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        var merged = new List<int>((left.Length + right.Length) / 2);
        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length || rightIndex < right.Length)
        {
            int start;
            int end;
            if (rightIndex >= right.Length ||
                leftIndex < left.Length && left[leftIndex] <= right[rightIndex])
            {
                start = left[leftIndex];
                end = left[leftIndex + 1];
                leftIndex += 2;
            }
            else
            {
                start = right[rightIndex];
                end = right[rightIndex + 1];
                rightIndex += 2;
            }

            AddMergedRange(merged, start, end);
        }

        return [.. merged];
    }

    public static byte[] CreateFirstByteLookup(ReadOnlySpan<int> ranges)
    {
        byte[] lookup = new byte[256];
        for (int rangeIndex = 0; rangeIndex < ranges.Length; rangeIndex += 2)
        {
            for (int value = ranges[rangeIndex]; value <= ranges[rangeIndex + 1]; value++)
            {
                lookup[Utf8FirstByte(value)] = 1;
            }
        }

        return lookup;
    }

    private static void AddMergedRange(List<int> merged, int start, int end)
    {
        if (merged.Count == 0)
        {
            merged.Add(start);
            merged.Add(end);
            return;
        }

        int lastEndIndex = merged.Count - 1;
        if (start <= merged[lastEndIndex] + 1)
        {
            merged[lastEndIndex] = Math.Max(merged[lastEndIndex], end);
            return;
        }

        merged.Add(start);
        merged.Add(end);
    }

    private static byte Utf8FirstByte(int value)
    {
        if (value <= 0x7F)
        {
            return (byte)value;
        }

        if (value <= 0x7FF)
        {
            return (byte)(0xC0 | (value >> 6));
        }

        if (value <= 0xFFFF)
        {
            return (byte)(0xE0 | (value >> 12));
        }

        return (byte)(0xF0 | (value >> 18));
    }
}
