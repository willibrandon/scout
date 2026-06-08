namespace Scout;

internal sealed class SignatureArityAccelerator
{
    private readonly byte[][] prefixes;
    private readonly byte[] prefixInitials;
    private readonly bool[] preOpenTerminators;
    private readonly int requiredCommas;

    public SignatureArityAccelerator(byte[][] prefixes, bool[] preOpenTerminators, int requiredCommas)
    {
        this.prefixes = prefixes;
        prefixInitials = BuildPrefixInitials(prefixes);
        this.preOpenTerminators = preOpenTerminators;
        this.requiredCommas = requiredCommas;
    }

    public long Count(ReadOnlySpan<byte> bytes, ulong? maxCount)
    {
        long count = 0;
        int offset = 0;
        while (TryFindNext(bytes, offset, out RegexMatch match))
        {
            count++;
            if (maxCount is ulong limit && (ulong)count >= limit)
            {
                return count;
            }

            offset = match.Start + match.Length;
        }

        return count;
    }

    public bool TryFindNext(ReadOnlySpan<byte> bytes, int startAt, out RegexMatch match)
    {
        int searchAt = Math.Clamp(startAt, 0, bytes.Length);
        while (searchAt < bytes.Length &&
            TryFindNextPrefix(bytes, searchAt, out int matchStart, out int prefixLength))
        {
            int prefixEnd = matchStart + prefixLength;
            int preOpenEnd = FindPreOpenEnd(bytes, prefixEnd);
            if (TryFindMatchEnd(bytes, prefixEnd, preOpenEnd, out int matchEnd))
            {
                match = new RegexMatch(matchStart, matchEnd - matchStart);
                return true;
            }

            searchAt = matchStart + 1;
        }

        match = default;
        return false;
    }

    private bool TryFindNextPrefix(ReadOnlySpan<byte> bytes, int startAt, out int matchStart, out int prefixLength)
    {
        int searchAt = startAt;
        while (searchAt < bytes.Length)
        {
            int relative = IndexOfAnyPrefixInitial(bytes[searchAt..]);
            if (relative < 0)
            {
                break;
            }

            int candidate = searchAt + relative;
            for (int index = 0; index < prefixes.Length; index++)
            {
                byte[] prefix = prefixes[index];
                if (bytes[candidate..].StartsWith(prefix))
                {
                    matchStart = candidate;
                    prefixLength = prefix.Length;
                    return true;
                }
            }

            searchAt = candidate + 1;
        }

        matchStart = -1;
        prefixLength = 0;
        return false;
    }

    private int IndexOfAnyPrefixInitial(ReadOnlySpan<byte> bytes)
    {
        return prefixInitials.Length switch
        {
            0 => -1,
            1 => bytes.IndexOf(prefixInitials[0]),
            2 => bytes.IndexOfAny(prefixInitials[0], prefixInitials[1]),
            3 => bytes.IndexOfAny(prefixInitials[0], prefixInitials[1], prefixInitials[2]),
            _ => IndexOfAnyPrefixInitialSlow(bytes),
        };
    }

    private int IndexOfAnyPrefixInitialSlow(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            for (int initialIndex = 0; initialIndex < prefixInitials.Length; initialIndex++)
            {
                if (bytes[index] == prefixInitials[initialIndex])
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static byte[] BuildPrefixInitials(byte[][] prefixes)
    {
        var initials = new List<byte>();
        for (int index = 0; index < prefixes.Length; index++)
        {
            byte initial = prefixes[index][0];
            if (!initials.Contains(initial))
            {
                initials.Add(initial);
            }
        }

        return initials.ToArray();
    }

    private int FindPreOpenEnd(ReadOnlySpan<byte> bytes, int start)
    {
        int index = start;
        while (index < bytes.Length && !preOpenTerminators[bytes[index]])
        {
            index++;
        }

        return index;
    }

    private bool TryFindMatchEnd(ReadOnlySpan<byte> bytes, int prefixEnd, int preOpenEnd, out int matchEnd)
    {
        int openSearchLength = preOpenEnd - prefixEnd;
        while (openSearchLength > 0)
        {
            int relativeOpen = bytes.Slice(prefixEnd, openSearchLength).LastIndexOf((byte)'(');
            if (relativeOpen < 0)
            {
                break;
            }

            int open = prefixEnd + relativeOpen;
            int relativeClose = bytes[(open + 1)..].IndexOf((byte)')');
            if (relativeClose >= 0)
            {
                int close = open + 1 + relativeClose;
                if (HasRequiredCommas(bytes[(open + 1)..close]))
                {
                    matchEnd = close + 1;
                    return true;
                }
            }

            openSearchLength = relativeOpen;
        }

        matchEnd = 0;
        return false;
    }

    private bool HasRequiredCommas(ReadOnlySpan<byte> bytes)
    {
        int commas = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)',')
            {
                continue;
            }

            commas++;
            if (commas >= requiredCommas)
            {
                return true;
            }
        }

        return false;
    }
}
