namespace Scout;

/// <summary>
/// Partitions independent records into maximal ASCII runs and individual non-ASCII records.
/// </summary>
/// <param name="records">The complete independent records to partition.</param>
/// <param name="terminator">The line-feed or NUL record terminator.</param>
internal ref struct AsciiRecordRunEnumerator(ReadOnlySpan<byte> records, byte terminator)
{
    private readonly ReadOnlySpan<byte> _records = records;
    private readonly byte _terminator = ValidateTerminator(terminator);
    private int _nextOffset;
    private int _knownNonAsciiOffset = -1;

    /// <summary>
    /// Gets the current ASCII run or non-ASCII record.
    /// </summary>
    public AsciiRecordRun Current { get; private set; }

    /// <summary>
    /// Advances to the next maximal ASCII run or individual non-ASCII record.
    /// </summary>
    /// <returns><see langword="true" /> when another run is available.</returns>
    public bool MoveNext()
    {
        if (_nextOffset >= _records.Length)
        {
            return false;
        }

        if (_knownNonAsciiOffset >= 0)
        {
            SetNonAsciiRecord(_knownNonAsciiOffset);
            return true;
        }

        ReadOnlySpan<byte> remaining = _records[_nextOffset..];
        int relativeNonAsciiOffset = remaining.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F);
        if (relativeNonAsciiOffset < 0)
        {
            Current = new AsciiRecordRun(_nextOffset, remaining.Length, isAscii: true);
            _nextOffset = _records.Length;
            return true;
        }

        int relativeRecordOffset = remaining[..relativeNonAsciiOffset].LastIndexOf(_terminator) + 1;
        if (relativeRecordOffset > 0)
        {
            _knownNonAsciiOffset = _nextOffset + relativeNonAsciiOffset;
            Current = new AsciiRecordRun(_nextOffset, relativeRecordOffset, isAscii: true);
            _nextOffset += relativeRecordOffset;
            return true;
        }

        SetNonAsciiRecord(_nextOffset + relativeNonAsciiOffset);
        return true;
    }

    private void SetNonAsciiRecord(int nonAsciiOffset)
    {
        ReadOnlySpan<byte> remainder = _records[nonAsciiOffset..];
        int relativeTerminatorOffset = remainder.IndexOf(_terminator);
        int recordEnd = relativeTerminatorOffset < 0
            ? _records.Length
            : nonAsciiOffset + relativeTerminatorOffset + 1;

        Current = new AsciiRecordRun(_nextOffset, recordEnd - _nextOffset, isAscii: false);
        _nextOffset = recordEnd;
        _knownNonAsciiOffset = -1;
    }

    private static byte ValidateTerminator(byte terminator)
    {
        if (terminator is not (byte)'\n' and not 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminator),
                terminator,
                "The record terminator must be line feed or NUL.");
        }

        return terminator;
    }
}
