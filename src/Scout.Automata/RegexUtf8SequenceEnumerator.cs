using System.Text;

namespace Scout;

/// <summary>
/// Decomposes a scalar range into lexicographically ordered, disjoint UTF-8 byte sequences.
/// </summary>
/// <param name="start">The inclusive lower scalar.</param>
/// <param name="end">The inclusive upper scalar.</param>
internal ref struct RegexUtf8SequenceEnumerator(int start, int end)
{
    private const int MaximumUtf8ByteCount = 4;
    // At most one suffix is pending for each UTF-8 width boundary, alignment split,
    // and the surrogate gap while the current lower range is decomposed.
    private const int MaximumPendingRangeCount = 8;
    private const int MaximumScalar = 0x10FFFF;
    private RegexScalarRange _pending0 = new(start, end);
    private RegexScalarRange _pending1;
    private RegexScalarRange _pending2;
    private RegexScalarRange _pending3;
    private RegexScalarRange _pending4;
    private RegexScalarRange _pending5;
    private RegexScalarRange _pending6;
    private RegexScalarRange _pending7;
    private int _pendingCount = 1;

    /// <summary>
    /// Attempts to read the next ordered UTF-8 byte sequence.
    /// </summary>
    /// <param name="sequence">Receives the next sequence when one remains.</param>
    /// <returns><see langword="true" /> when a sequence was produced.</returns>
    public bool MoveNext(out RegexUtf8ByteSequence sequence)
    {
        while (_pendingCount > 0)
        {
            RegexScalarRange range = Pop();
            while (true)
            {
                if (TrySplitSurrogates(range, out RegexScalarRange lower, out RegexScalarRange upper))
                {
                    Push(upper);
                    range = lower;
                    continue;
                }

                if (!IsValid(range))
                {
                    break;
                }

                bool split = false;
                for (int byteCount = 1; byteCount < MaximumUtf8ByteCount; byteCount++)
                {
                    int maximum = GetMaximumScalar(byteCount);
                    if (range.Start <= maximum && maximum < range.End)
                    {
                        Push(new RegexScalarRange(maximum + 1, range.End));
                        range = new RegexScalarRange(range.Start, maximum);
                        split = true;
                        break;
                    }
                }

                if (split)
                {
                    continue;
                }

                if (range.End <= 0x7F)
                {
                    sequence = RegexUtf8ByteSequence.Create(
                        new RegexUtf8ByteRange((byte)range.Start, (byte)range.End));
                    return true;
                }

                for (int index = 1; index < MaximumUtf8ByteCount; index++)
                {
                    int mask = (1 << (6 * index)) - 1;
                    if ((range.Start & ~mask) == (range.End & ~mask))
                    {
                        continue;
                    }

                    if ((range.Start & mask) != 0)
                    {
                        int lowerEnd = range.Start | mask;
                        Push(new RegexScalarRange(lowerEnd + 1, range.End));
                        range = new RegexScalarRange(range.Start, lowerEnd);
                        split = true;
                        break;
                    }

                    if ((range.End & mask) != mask)
                    {
                        int upperStart = range.End & ~mask;
                        Push(new RegexScalarRange(upperStart, range.End));
                        range = new RegexScalarRange(range.Start, upperStart - 1);
                        split = true;
                        break;
                    }
                }

                if (split)
                {
                    continue;
                }

                Span<byte> encodedStart = stackalloc byte[MaximumUtf8ByteCount];
                Span<byte> encodedEnd = stackalloc byte[MaximumUtf8ByteCount];
                int startLength = new Rune(range.Start).EncodeToUtf8(encodedStart);
                int endLength = new Rune(range.End).EncodeToUtf8(encodedEnd);
                if (startLength != endLength)
                {
                    throw new InvalidOperationException("UTF-8 decomposition crossed an encoding length boundary.");
                }

                sequence = RegexUtf8ByteSequence.Create(
                    encodedStart[..startLength],
                    encodedEnd[..endLength]);
                return true;
            }
        }

        sequence = default;
        return false;
    }

    private static bool IsValid(RegexScalarRange range)
    {
        return range.Start >= 0 && range.Start <= range.End && range.End <= MaximumScalar;
    }

    private static bool TrySplitSurrogates(
        RegexScalarRange range,
        out RegexScalarRange lower,
        out RegexScalarRange upper)
    {
        if (range.Start < 0xE000 && range.End > 0xD7FF)
        {
            lower = new RegexScalarRange(range.Start, 0xD7FF);
            upper = new RegexScalarRange(0xE000, range.End);
            return true;
        }

        lower = default;
        upper = default;
        return false;
    }

    private static int GetMaximumScalar(int byteCount)
    {
        return byteCount switch
        {
            1 => 0x7F,
            2 => 0x7FF,
            3 => 0xFFFF,
            4 => MaximumScalar,
            _ => throw new ArgumentOutOfRangeException(nameof(byteCount)),
        };
    }

    private RegexScalarRange Pop()
    {
        _pendingCount--;
        return GetPending(_pendingCount);
    }

    private void Push(RegexScalarRange range)
    {
        if (_pendingCount >= MaximumPendingRangeCount)
        {
            throw new InvalidOperationException("UTF-8 range decomposition exceeded its pending-range bound.");
        }

        SetPending(_pendingCount, range);
        _pendingCount++;
    }

    private readonly RegexScalarRange GetPending(int index)
    {
        return index switch
        {
            0 => _pending0,
            1 => _pending1,
            2 => _pending2,
            3 => _pending3,
            4 => _pending4,
            5 => _pending5,
            6 => _pending6,
            7 => _pending7,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    private void SetPending(int index, RegexScalarRange range)
    {
        switch (index)
        {
            case 0:
                _pending0 = range;
                break;
            case 1:
                _pending1 = range;
                break;
            case 2:
                _pending2 = range;
                break;
            case 3:
                _pending3 = range;
                break;
            case 4:
                _pending4 = range;
                break;
            case 5:
                _pending5 = range;
                break;
            case 6:
                _pending6 = range;
                break;
            case 7:
                _pending7 = range;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
