namespace Scout;

internal struct CapturingMatchSink : IMatchSink
{
    public ulong Matches { get; private set; }

    public long LineNumber { get; private set; }

    public long ByteOffset { get; private set; }

    public long MatchColumn { get; private set; }

    public byte[] FirstMatch { get; private set; }

    public long FirstByteOffset { get; private set; }

    public byte[] Match { get; private set; }

    public void Matched(
        long lineNumber,
        long byteOffset,
        long matchColumn,
        ReadOnlySpan<byte> match)
    {
        if (Matches == 0)
        {
            FirstByteOffset = byteOffset;
            FirstMatch = match.ToArray();
        }

        Matches++;
        LineNumber = lineNumber;
        ByteOffset = byteOffset;
        MatchColumn = matchColumn;
        Match = match.ToArray();
    }
}
